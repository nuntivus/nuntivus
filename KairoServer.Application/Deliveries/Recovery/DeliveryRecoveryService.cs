using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Recovery
{
    public sealed class DeliveryRecoveryService : IDeliveryRecoveryService
    {
        public const int MaxPageSize = 100;

        private readonly IDeliveryRepository _deliveries;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<DeliveryRecoveryService> _logger;

        public DeliveryRecoveryService(
            IDeliveryRepository deliveries,
            IUnitOfWork unitOfWork,
            IAppLogger<DeliveryRecoveryService> logger)
        {
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<PagedResult<DeliveryRecoveryView>>> ListDeadAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (page < 1)
                return Result.Failure<PagedResult<DeliveryRecoveryView>>(Error.Validation("page must be greater than or equal to 1."));
            if (pageSize < 1 || pageSize > MaxPageSize)
                return Result.Failure<PagedResult<DeliveryRecoveryView>>(
                    Error.Validation($"pageSize must be between 1 and {MaxPageSize}."));

            var list = await _deliveries.ListDeadAsync(page, pageSize, cancellationToken);
            if (list.IsFailure)
                return Result<PagedResult<DeliveryRecoveryView>>.Fail(list.Errors);

            return Result.Success(list.Value.Map(ToView));
        }

        public Task<Result<DeliveryRecoveryView>> ManualRetryAsync(
            Guid deliveryId,
            ManualDeliveryOperationRequest request,
            CancellationToken cancellationToken) =>
            RecoverAsync(deliveryId, "retry", request, (delivery, now) => delivery.ManualRetryReset(now), cancellationToken);

        public Task<Result<DeliveryRecoveryView>> ManualRequeueAsync(
            Guid deliveryId,
            ManualDeliveryOperationRequest request,
            CancellationToken cancellationToken) =>
            RecoverAsync(deliveryId, "requeue", request, (delivery, now) => delivery.ManualRequeue(now), cancellationToken);

        public Task<Result<DeliveryRecoveryView>> ManualCancelAsync(
            Guid deliveryId,
            ManualDeliveryOperationRequest request,
            CancellationToken cancellationToken)
        {
            var reason = string.IsNullOrWhiteSpace(request.Reason)
                ? "Canceled by operator."
                : request.Reason;

            return RecoverAsync(
                deliveryId,
                "cancel",
                request,
                (delivery, now) => delivery.ManualCancel(reason!, now),
                cancellationToken);
        }

        public async Task<Result<DeliveryFailureView>> GetFailureAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            if (deliveryId == Guid.Empty)
                return Result.Failure<DeliveryFailureView>(Error.Validation("deliveryId is required."));

            var lookup = await _deliveries.GetByIdAsync(deliveryId, cancellationToken);
            if (lookup.IsFailure)
                return Result<DeliveryFailureView>.Fail(lookup.Errors);

            var delivery = lookup.Value;
            if (delivery is null)
                return Result.Failure<DeliveryFailureView>(Error.NotFound($"Delivery '{deliveryId}' was not found."));

            return Result.Success(new DeliveryFailureView(
                delivery.Id,
                delivery.Status,
                delivery.LastError,
                delivery.NextAttemptAt,
                delivery.UpdatedAt));
        }

        public async Task<Result<IReadOnlyList<DeliveryAttemptView>>> GetAttemptsAsync(
            Guid deliveryId,
            CancellationToken cancellationToken)
        {
            if (deliveryId == Guid.Empty)
                return Result.Failure<IReadOnlyList<DeliveryAttemptView>>(Error.Validation("deliveryId is required."));

            var lookup = await _deliveries.GetByIdAsync(deliveryId, cancellationToken);
            if (lookup.IsFailure)
                return Result<IReadOnlyList<DeliveryAttemptView>>.Fail(lookup.Errors);

            if (lookup.Value is null)
                return Result.Failure<IReadOnlyList<DeliveryAttemptView>>(Error.NotFound($"Delivery '{deliveryId}' was not found."));

            var attempts = await _deliveries.GetAttemptsByDeliveryIdAsync(deliveryId, cancellationToken);
            if (attempts.IsFailure)
                return Result<IReadOnlyList<DeliveryAttemptView>>.Fail(attempts.Errors);

            var mapped = new List<DeliveryAttemptView>(attempts.Value.Count);
            foreach (var attempt in attempts.Value)
            {
                mapped.Add(new DeliveryAttemptView(
                    attempt.Id,
                    attempt.AttemptNumber,
                    attempt.Succeeded,
                    attempt.FailureCategory,
                    attempt.Error,
                    attempt.Metadata,
                    attempt.StartedAt,
                    attempt.CompletedAt));
            }

            return Result.Success<IReadOnlyList<DeliveryAttemptView>>(mapped);
        }

        private async Task<Result<DeliveryRecoveryView>> RecoverAsync(
            Guid deliveryId,
            string operation,
            ManualDeliveryOperationRequest request,
            Func<Delivery, DateTimeOffset, bool> transition,
            CancellationToken cancellationToken)
        {
            if (deliveryId == Guid.Empty)
                return Result.Failure<DeliveryRecoveryView>(Error.Validation("deliveryId is required."));
            if (request.OperationId == Guid.Empty)
                return Result.Failure<DeliveryRecoveryView>(Error.Validation("operationId is required."));
            if (string.IsNullOrWhiteSpace(request.OperatorId))
                return Result.Failure<DeliveryRecoveryView>(Error.Validation("operatorId is required."));
            if (string.IsNullOrWhiteSpace(request.RequestSnapshot))
                return Result.Failure<DeliveryRecoveryView>(Error.Validation("requestSnapshot is required."));

            try
            {
                while (true)
                {
                    var lookup = await _deliveries.GetByIdAsync(deliveryId, cancellationToken);
                    if (lookup.IsFailure)
                        return Result<DeliveryRecoveryView>.Fail(lookup.Errors);

                    var delivery = lookup.Value;
                    if (delivery is null)
                        return Result.Failure<DeliveryRecoveryView>(Error.NotFound($"Delivery '{deliveryId}' was not found."));

                    var duplicate = await _deliveries.GetOperationAuditAsync(deliveryId, request.OperationId, cancellationToken);
                    if (duplicate.IsFailure)
                        return Result<DeliveryRecoveryView>.Fail(duplicate.Errors);

                    if (duplicate.Value is not null)
                    {
                        return Result.Success(ToView(delivery));
                    }

                    var expectedUpdatedAt = delivery.UpdatedAt;
                    var now = DateTimeOffset.UtcNow;
                    bool changed;
                    try
                    {
                        changed = transition(delivery, now);
                    }
                    catch (InvalidOperationException)
                    {
                        return Result.Failure<DeliveryRecoveryView>(Error.Conflict(
                            $"Cannot {operation} delivery '{deliveryId}' from status '{delivery.Status}'."));
                    }

                    var view = ToView(delivery);
                    var responseSnapshot = JsonSerializer.Serialize(view);
                    var audit = DeliveryOperationAudit.Create(
                        deliveryId,
                        request.OperationId,
                        operation,
                        request.OperatorId,
                        request.RequestSnapshot,
                        responseSnapshot,
                        now);

                    _unitOfWork.Begin(true);
                    if (changed)
                    {
                        var update = await _deliveries.UpdateAsync(
                            delivery,
                            expectedUpdatedAt,
                            _unitOfWork,
                            cancellationToken);
                        if (update.IsFailure)
                        {
                            _unitOfWork.Rollback();
                            return Result<DeliveryRecoveryView>.Fail(update.Errors);
                        }

                        if (!update.Value)
                        {
                            _unitOfWork.Rollback();
                            continue;
                        }
                    }

                    var auditInsert = await _deliveries.AddOperationAuditAsync(audit, _unitOfWork, cancellationToken);
                    if (auditInsert.IsFailure)
                    {
                        _unitOfWork.Rollback();
                        return Result<DeliveryRecoveryView>.Fail(auditInsert.Errors);
                    }

                    if (auditInsert.Value == 0)
                    {
                        _unitOfWork.Rollback();
                        continue;
                    }

                    _unitOfWork.Commit();
                    _logger.Information(
                        "Manual delivery operation '{Operation}' applied by {OperatorId} for {DeliveryId}.",
                        operation,
                        request.OperatorId,
                        deliveryId);
                    return Result.Success(view);

                }
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed manual delivery operation '{Operation}' for {DeliveryId}.", operation, deliveryId);
                return Result.Failure<DeliveryRecoveryView>(ex);
            }
        }

        private static DeliveryRecoveryView ToView(Delivery delivery) =>
            new(
                delivery.Id,
                delivery.Status,
                delivery.AttemptCount,
                delivery.LastError,
                delivery.NextAttemptAt,
                delivery.UpdatedAt);
    }
}
