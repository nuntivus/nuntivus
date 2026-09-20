using System;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Deliveries.Health;
using KairoServer.Application.Observability;
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
        private readonly IContractDeliveryRateLimiter _rateLimiter;
        private readonly IDeliveryOrderingCoordinator _orderingCoordinator;
        private readonly IDeliveryDispatcherMetrics _metrics;
        private readonly IDeliveryExecutorDispatcher _executors;
        private readonly IDeliveryPayloadTransformer _payloadTransformer;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<DeliveryDispatcherService> _logger;
        private readonly DeliveryDispatcherOption _options;

        public DeliveryDispatcherService(
            IDeliveryRepository deliveries,
            IContractDeliveryRateLimiter rateLimiter,
            IDeliveryOrderingCoordinator orderingCoordinator,
            IDeliveryDispatcherMetrics metrics,
            IDeliveryExecutorDispatcher executors,
            IDeliveryPayloadTransformer payloadTransformer,
            IUnitOfWork unitOfWork,
            IAppLogger<DeliveryDispatcherService> logger,
            DeliveryDispatcherOption? options = null)
        {
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _orderingCoordinator = orderingCoordinator ?? throw new ArgumentNullException(nameof(orderingCoordinator));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _executors = executors ?? throw new ArgumentNullException(nameof(executors));
            _payloadTransformer = payloadTransformer ?? throw new ArgumentNullException(nameof(payloadTransformer));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new DeliveryDispatcherOption();
        }

        public async Task<Result<DeliveryDispatchOutcome>> DispatchAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            using var activity = KairoTelemetry.ActivitySource.StartActivity("delivery.dispatch", ActivityKind.Internal);
            activity?.SetTag(KairoTelemetry.DeliveryIdTag, deliveryId.ToString("D"));

            var loadResult = await _deliveries.GetByIdAsync(deliveryId, cancellationToken);
            if (loadResult.IsFailure)
                return Result.Failure<DeliveryDispatchOutcome>(loadResult.Error!);

            var delivery = loadResult.Value;
            if (delivery is null)
            {
                _logger.Warning("Delivery {DeliveryId} not found; treating as no-op.", deliveryId);
                return Result.Success(DeliveryDispatchOutcome.NotFound);
            }

            activity?.SetTag(KairoTelemetry.EventIdTag, delivery.EventId.ToString("D"));
            activity?.SetTag(KairoTelemetry.ContractIdTag, delivery.ContractId.ToString("D"));
            activity?.SetTag(KairoTelemetry.CustomerIdTag, delivery.CustomerId);

            if (delivery.Status is DeliveryStatus.Delivered)
            {
                return Result.Success(DeliveryDispatchOutcome.AlreadyTerminal);
            }

            if (delivery.Status is DeliveryStatus.Dead)
            {
                return Result.Success(DeliveryDispatchOutcome.AlreadyTerminal);
            }

            if (delivery.Status is DeliveryStatus.Failed && delivery.NextAttemptAt is null)
            {
                return Result.Success(DeliveryDispatchOutcome.AlreadyTerminal);
            }

            var now = DateTimeOffset.UtcNow;
            var expectedUpdatedAt = delivery.UpdatedAt;
            if (delivery.Status is DeliveryStatus.Failed
                && delivery.NextAttemptAt is { } nextAttemptAt
                && nextAttemptAt > now)
            {
                _logger.Information(
                    "Delivery {DeliveryId} retry deferred until {NextAttemptAt}; skipping early execution.",
                    delivery.Id, nextAttemptAt);
                await _metrics.TrackDispatchOutcomeAsync(
                    "retry_deferred",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                activity?.SetTag(KairoTelemetry.OutcomeTag, "retry_deferred");
                return Result.Success(DeliveryDispatchOutcome.Skipped);
            }

            var rateLimitDecision = await TryApplyRateLimitAsync(delivery, expectedUpdatedAt, now, cancellationToken);
            if (rateLimitDecision.IsFailure)
                return Result.Failure<DeliveryDispatchOutcome>(rateLimitDecision.Error!);

            if (!rateLimitDecision.Value)
            {
                await _metrics.TrackDispatchOutcomeAsync(
                    "throttled",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                activity?.SetTag(KairoTelemetry.OutcomeTag, "throttled");
                return Result.Success(DeliveryDispatchOutcome.Skipped);
            }

            var orderingDecision = await TryApplyOrderingAsync(delivery, cancellationToken);
            if (orderingDecision.IsFailure)
                return Result.Failure<DeliveryDispatchOutcome>(orderingDecision.Error!);

            if (!orderingDecision.Value)
            {
                await _metrics.TrackDispatchOutcomeAsync(
                    "ordering_blocked",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                activity?.SetTag(KairoTelemetry.OutcomeTag, "ordering_blocked");
                return Result.Success(DeliveryDispatchOutcome.Skipped);
            }

            if (delivery.OrderingEnabled)
            {
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
            }
            else
            {
                var claimResult = await _deliveries.TryClaimForDispatchAsync(
                    delivery.Id,
                    expectedUpdatedAt,
                    now,
                    cancellationToken);
                if (claimResult.IsFailure)
                    return Result.Failure<DeliveryDispatchOutcome>(claimResult.Error!);

                var claimedDelivery = claimResult.Value;
                if (claimedDelivery is null)
                {
                    _logger.Information(
                        "Delivery {DeliveryId} was already claimed/updated by another worker.",
                        delivery.Id);
                    await _metrics.TrackDispatchOutcomeAsync(
                        "concurrency_skipped",
                        delivery.EventId,
                        delivery.Id,
                        delivery.ContractId,
                        cancellationToken);
                    activity?.SetTag(KairoTelemetry.OutcomeTag, "concurrency_skipped");
                    return Result.Success(DeliveryDispatchOutcome.Skipped);
                }

                delivery = claimedDelivery;
                expectedUpdatedAt = delivery.UpdatedAt;
            }

            var attemptNumber = delivery.AttemptCount;
            var startedAt = DateTimeOffset.UtcNow;
            var payloadResult = _payloadTransformer.Apply(
                delivery.Payload,
                delivery.TransformationConfiguration);

            DeliveryExecutionResult execution;
            if (payloadResult.IsFailure)
            {
                execution = DeliveryExecutionResult.Failure(
                    DeliveryFailureCategory.ConfigurationInvalid,
                    payloadResult.Error?.Message ?? "Transformation failed.");
            }
            else
            {
                var request = new DeliveryExecutionRequest(
                    delivery.Id,
                    delivery.EventId,
                    delivery.ContractId,
                    delivery.CustomerId,
                    delivery.EventType,
                    payloadResult.Value,
                    attemptNumber,
                    new Destination(delivery.DestinationType, delivery.DestinationConfiguration));

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
            }
            var completedAt = DateTimeOffset.UtcNow;

            var (finalizedNow, retry) = ClassifyRetry(execution, attemptNumber, completedAt);
            var destinationLatencyMs = ResolveDestinationLatencyMs(execution);
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
            else if (retry is null)
            {
                delivery.MarkDead(execution.Error ?? "Delivery failed.", finalizedNow);
            }
            else
            {
                delivery.MarkFailed(execution.Error ?? "Delivery failed.", retry, finalizedNow);
            }

            _unitOfWork.Begin(true);
            DestinationHealthView? destinationHealth = null;
            try
            {
                var updateResult = await _deliveries.UpdateAsync(
                    delivery,
                    expectedUpdatedAt,
                    _unitOfWork,
                    cancellationToken);
                updateResult.EnsureSuccess();
                if (!updateResult.Value)
                {
                    _unitOfWork.Rollback();
                    _logger.Warning(
                        "Concurrent update detected for delivery {DeliveryId}; skipping duplicate dispatch result.",
                        delivery.Id);
                    await _metrics.TrackDispatchOutcomeAsync(
                        "concurrency_skipped",
                        delivery.EventId,
                        delivery.Id,
                        delivery.ContractId,
                        cancellationToken);
                    activity?.SetTag(KairoTelemetry.OutcomeTag, "concurrency_skipped");
                    return Result.Success(DeliveryDispatchOutcome.Skipped);
                }

                (await _deliveries.AddAttemptAsync(attempt, _unitOfWork, cancellationToken)).EnsureSuccess();

                var destinationHealthResult = await _deliveries.UpsertDestinationHealthAsync(
                    new DestinationHealthObservation(
                        delivery.ContractId,
                        delivery.CustomerId,
                        delivery.DestinationType.ToString(),
                        execution.Succeeded,
                        completedAt,
                        destinationLatencyMs),
                    _unitOfWork,
                    cancellationToken);
                destinationHealthResult.EnsureSuccess();
                destinationHealth = destinationHealthResult.Value;

                _unitOfWork.Commit();
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to persist delivery {DeliveryId} outcome.", delivery.Id);
                return Result.Failure<DeliveryDispatchOutcome>(ex);
            }

            var orderingUpdate = await _orderingCoordinator.RecordOutcomeAsync(delivery, cancellationToken);
            if (orderingUpdate.IsFailure)
                return Result.Failure<DeliveryDispatchOutcome>(orderingUpdate.Error!);

            if (execution.Succeeded)
            {
                _logger.Information(
                    "Delivered {DeliveryId} (event {EventId}) on attempt {Attempt}.",
                    delivery.Id, delivery.EventId, attemptNumber);
                var dispatchLatency = completedAt - startedAt;
                var deliveryLatency = completedAt - delivery.CreatedAt;

                await _metrics.TrackDispatchLatencyAsync(
                    dispatchLatency,
                    "delivered",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                await _metrics.TrackDeliveryLatencyAsync(
                    deliveryLatency,
                    "delivered",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                await TryTrackDestinationLatencyAsync(delivery, execution, "delivered", cancellationToken);
                await TryTrackDestinationHealthStatusAsync(destinationHealth, cancellationToken);
                await _metrics.TrackDispatchOutcomeAsync(
                    "delivered",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                activity?.SetTag(KairoTelemetry.OutcomeTag, "delivered");
                return Result.Success(DeliveryDispatchOutcome.Delivered);
            }

            if (retry is null)
            {
                _logger.Warning(
                    "Delivery {DeliveryId} exhausted (attempt {Attempt}, category {Category}): {Error}.",
                    delivery.Id, attemptNumber, execution.Category, execution.Error);
                var dispatchLatency = completedAt - startedAt;
                var deliveryLatency = completedAt - delivery.CreatedAt;

                await _metrics.TrackDispatchLatencyAsync(
                    dispatchLatency,
                    "failed_terminal",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                await _metrics.TrackDeliveryLatencyAsync(
                    deliveryLatency,
                    "failed_terminal",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                await TryTrackDestinationLatencyAsync(delivery, execution, "failed_terminal", cancellationToken);
                await TryTrackDestinationHealthStatusAsync(destinationHealth, cancellationToken);
                await _metrics.TrackDeadDeliveryCountAsync(
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                await _metrics.TrackDispatchOutcomeAsync(
                    "failed_terminal",
                    delivery.EventId,
                    delivery.Id,
                    delivery.ContractId,
                    cancellationToken);
                activity?.SetTag(KairoTelemetry.OutcomeTag, "failed_terminal");
                return Result.Success(DeliveryDispatchOutcome.FailedTerminal);
            }

            _logger.Warning(
                "Delivery {DeliveryId} failed attempt {Attempt} ({Category}); retry at {RetryAt}: {Error}.",
                delivery.Id, attemptNumber, execution.Category, retry, execution.Error);
            var retryDispatchLatency = completedAt - startedAt;
            var retryDeliveryLatency = completedAt - delivery.CreatedAt;

            await _metrics.TrackDispatchLatencyAsync(
                retryDispatchLatency,
                "failed_retry",
                delivery.EventId,
                delivery.Id,
                delivery.ContractId,
                cancellationToken);
            await _metrics.TrackDeliveryLatencyAsync(
                retryDeliveryLatency,
                "failed_retry",
                delivery.EventId,
                delivery.Id,
                delivery.ContractId,
                cancellationToken);
            await TryTrackDestinationLatencyAsync(delivery, execution, "failed_retry", cancellationToken);
            await TryTrackDestinationHealthStatusAsync(destinationHealth, cancellationToken);
            await _metrics.TrackRetryCountAsync(
                delivery.EventId,
                delivery.Id,
                delivery.ContractId,
                cancellationToken);
            await _metrics.TrackDispatchOutcomeAsync(
                "failed_retry",
                delivery.EventId,
                delivery.Id,
                delivery.ContractId,
                cancellationToken);
            activity?.SetTag(KairoTelemetry.OutcomeTag, "failed_retry");
            return Result.Success(DeliveryDispatchOutcome.FailedRetry);
        }

        private async ValueTask TryTrackDestinationLatencyAsync(
            Delivery delivery,
            DeliveryExecutionResult execution,
            string outcome,
            CancellationToken cancellationToken)
        {
            if (execution.Metadata is null)
                return;

            if (!execution.Metadata.TryGetValue("destinationLatencyMs", out var rawValue))
                return;

            var destinationLatencyMs = ResolveDestinationLatencyMs(execution);
            if (!destinationLatencyMs.HasValue)
                return;

            await _metrics.TrackDestinationLatencyAsync(
                delivery.DestinationType.ToString().ToLowerInvariant(),
                TimeSpan.FromMilliseconds(destinationLatencyMs.Value),
                outcome,
                delivery.EventId,
                delivery.Id,
                delivery.ContractId,
                cancellationToken);
        }

        private static double? ResolveDestinationLatencyMs(DeliveryExecutionResult execution)
        {
            if (execution.Metadata is null)
                return null;

            if (!execution.Metadata.TryGetValue("destinationLatencyMs", out var rawValue))
                return null;

            return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private async ValueTask TryTrackDestinationHealthStatusAsync(
            DestinationHealthView? destinationHealth,
            CancellationToken cancellationToken)
        {
            if (destinationHealth is null)
                return;

            await _metrics.TrackDestinationHealthStatusAsync(
                destinationHealth.Status.ToString().ToLowerInvariant(),
                destinationHealth.CustomerId,
                destinationHealth.ContractId,
                cancellationToken);
        }

        private async Task<Result<bool>> TryApplyOrderingAsync(
            Delivery delivery,
            CancellationToken cancellationToken)
        {
            if (!delivery.OrderingEnabled || string.IsNullOrWhiteSpace(delivery.OrderingKey))
                return Result.Success(true);

            var gateResult = await _orderingCoordinator.TryAcquireAsync(delivery, cancellationToken);
            if (gateResult.IsFailure)
                return Result.Failure<bool>(gateResult.Error!);

            if (gateResult.Value.Granted)
                return Result.Success(true);

            _logger.Information(
                "Delivery {DeliveryId} blocked by ordering key {OrderingKey}; waiting for prior delivery.",
                delivery.Id,
                delivery.OrderingKey);

            return Result.Success(false);
        }

        private async Task<Result<bool>> TryApplyRateLimitAsync(
            Delivery delivery,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            if (delivery.RateLimit is null || !delivery.RateLimit.IsEnabled)
                return Result.Success(true);

            _unitOfWork.Begin(true);
            try
            {
                var permitResult = await _rateLimiter.TryAcquirePermitAsync(
                    delivery.ContractId,
                    delivery.RateLimit,
                    now,
                    cancellationToken);
                permitResult.EnsureSuccess();

                if (permitResult.Value.Granted)
                {
                    _unitOfWork.Commit();
                    await _metrics.TrackRateLimitDecisionAsync("granted", cancellationToken);
                    return Result.Success(true);
                }

                var nextAllowedAt = permitResult.Value.NextAllowedAt ?? now.AddSeconds(1);
                var scheduleResult = await _deliveries.ScheduleNextAttemptAsync(
                    delivery.Id,
                    expectedUpdatedAt,
                    nextAllowedAt,
                    _unitOfWork,
                    cancellationToken);
                scheduleResult.EnsureSuccess();

                if (!scheduleResult.Value)
                {
                    _unitOfWork.Rollback();
                    await _metrics.TrackRateLimitDecisionAsync("stale", cancellationToken);
                    return Result.Success(false);
                }

                _unitOfWork.Commit();
                var delay = nextAllowedAt - now;
                await _metrics.TrackRateLimitDelayAsync(delay, cancellationToken);
                await _metrics.TrackRateLimitDecisionAsync("throttled", cancellationToken);
                _logger.Information(
                    "Delivery {DeliveryId} delayed by rate limiter until {NextAttemptAt} (contract {ContractId}).",
                    delivery.Id,
                    nextAllowedAt,
                    delivery.ContractId);
                return Result.Success(false);
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                return Result.Failure<bool>(ex);
            }
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
            var delay = TimeSpan.FromTicks(ticks);

            if (_options.JitterRatio <= 0)
            {
                return delay;
            }

            var ratio = Math.Min(_options.JitterRatio, 1d);
            var min = 1d - ratio;
            var max = 1d + ratio;
            var factor = min + (Random.Shared.NextDouble() * (max - min));
            var jitteredTicks = Math.Max(1L, (long)(delay.Ticks * factor));
            return TimeSpan.FromTicks(jitteredTicks);
        }
    }
}
