using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Orchestrates a single delivery attempt: loads the <see cref="Delivery"/>,
    /// invokes the destination executor, records a <see cref="DeliveryAttempt"/>,
    /// and updates <see cref="Delivery"/> state (Delivered / Failed w/ retry /
    /// Failed terminal). All persistence happens in one transaction so the
    /// attempt row and the delivery row can never disagree.
    /// </summary>
    public sealed class DeliveryDispatcherService : IDeliveryDispatcherService
    {
        private readonly IDeliveryRepository _deliveries;
        private readonly IDeliveryExecutorDispatcher _executors;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<DeliveryDispatcherService> _logger;
        private readonly DeliveryDispatcherOption _options;

        public DeliveryDispatcherService(
            IDeliveryRepository deliveries,
            IDeliveryExecutorDispatcher executors,
            IUnitOfWork unitOfWork,
            IAppLogger<DeliveryDispatcherService> logger,
            DeliveryDispatcherOption? options = null)
        {
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
            _executors = executors ?? throw new ArgumentNullException(nameof(executors));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new DeliveryDispatcherOption();
        }

        public async Task<Result<DeliveryDispatchOutcome>> DispatchAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            var loadResult = await _deliveries.GetByIdAsync(deliveryId, cancellationToken);
            if (loadResult.IsFailure)
                return Result.Failure<DeliveryDispatchOutcome>(loadResult.Error!);

            var delivery = loadResult.Value;
            if (delivery is null)
            {
                _logger.Warning("Delivery {DeliveryId} not found; treating as no-op.", deliveryId);
                return Result.Success(DeliveryDispatchOutcome.NotFound);
            }

            if (delivery.Status is DeliveryStatus.Delivered)
            {
                return Result.Success(DeliveryDispatchOutcome.AlreadyTerminal);
            }

            if (delivery.Status is DeliveryStatus.Failed && delivery.NextAttemptAt is null)
            {
                return Result.Success(DeliveryDispatchOutcome.AlreadyTerminal);
            }

            var now = DateTimeOffset.UtcNow;
            try
            {
                delivery.MarkInFlight(now);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warning(
                    "Delivery {DeliveryId} cannot transition to InFlight ({Status}): {Error}.",
                    delivery.Id, delivery.Status, ex.Message);
                return Result.Success(DeliveryDispatchOutcome.Skipped);
            }

            var attemptNumber = delivery.AttemptCount;
            var request = new DeliveryExecutionRequest(
                delivery.Id,
                delivery.EventId,
                delivery.ContractId,
                delivery.CustomerId,
                delivery.EventType,
                delivery.Payload,
                attemptNumber,
                new Destination(delivery.DestinationType, delivery.DestinationConfiguration));

            var startedAt = DateTimeOffset.UtcNow;
            DeliveryExecutionResult execution;
            try
            {
                execution = await _executors.ExecuteAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Unhandled error while executing delivery {DeliveryId} attempt {Attempt}.",
                    delivery.Id, attemptNumber);
                execution = DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.Transient,
                    $"Executor threw: {ex.Message}");
            }
            var completedAt = DateTimeOffset.UtcNow;

            var (finalizedNow, retry) = ClassifyRetry(execution, attemptNumber, completedAt);
            var attempt = DeliveryAttempt.Create(
                delivery.Id,
                attemptNumber,
                execution.Succeeded,
                execution.Category?.ToString(),
                execution.Error,
                execution.Metadata,
                startedAt,
                completedAt);

            if (execution.Succeeded)
            {
                delivery.MarkDelivered(finalizedNow);
            }
            else
            {
                delivery.MarkFailed(execution.Error ?? "Delivery failed.", retry, finalizedNow);
            }

            _unitOfWork.Begin(true);
            try
            {
                (await _deliveries.UpdateAsync(delivery, _unitOfWork, cancellationToken)).EnsureSuccess();
                (await _deliveries.AddAttemptAsync(attempt, _unitOfWork, cancellationToken)).EnsureSuccess();
                _unitOfWork.Commit();
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to persist delivery {DeliveryId} outcome.", delivery.Id);
                return Result.Failure<DeliveryDispatchOutcome>(ex);
            }

            if (execution.Succeeded)
            {
                _logger.Information(
                    "Delivered {DeliveryId} (event {EventId}) on attempt {Attempt}.",
                    delivery.Id, delivery.EventId, attemptNumber);
                return Result.Success(DeliveryDispatchOutcome.Delivered);
            }

            if (retry is null)
            {
                _logger.Warning(
                    "Delivery {DeliveryId} exhausted (attempt {Attempt}, category {Category}): {Error}.",
                    delivery.Id, attemptNumber, execution.Category, execution.Error);
                return Result.Success(DeliveryDispatchOutcome.FailedTerminal);
            }

            _logger.Warning(
                "Delivery {DeliveryId} failed attempt {Attempt} ({Category}); retry at {RetryAt}: {Error}.",
                delivery.Id, attemptNumber, execution.Category, retry, execution.Error);
            return Result.Success(DeliveryDispatchOutcome.FailedRetry);
        }

        private (DateTimeOffset now, DateTimeOffset? nextAttempt) ClassifyRetry(
            DeliveryExecutionResult execution,
            int attemptNumber,
            DateTimeOffset completedAt)
        {
            if (execution.Succeeded)
                return (completedAt, null);

            // Non-retryable failure categories.
            if (execution.Category is DeliveryFailureCategory.Permanent
                or DeliveryFailureCategory.ConfigurationInvalid)
            {
                return (completedAt, null);
            }

            if (attemptNumber >= _options.MaxAttempts)
            {
                return (completedAt, null);
            }

            var backoff = execution.RetryAfter is { } hint && hint > TimeSpan.Zero
                ? hint
                : ComputeBackoff(attemptNumber);

            if (backoff > _options.MaxBackoff)
                backoff = _options.MaxBackoff;

            return (completedAt, completedAt.Add(backoff));
        }

        private TimeSpan ComputeBackoff(int attemptNumber)
        {
            // Exponential backoff: base * 2^(attempt-1), clamped by MaxBackoff.
            var exp = Math.Min(attemptNumber - 1, 10);
            var ticks = _options.BaseBackoff.Ticks * (long)Math.Pow(2, exp);
            return TimeSpan.FromTicks(ticks);
        }
    }
}
