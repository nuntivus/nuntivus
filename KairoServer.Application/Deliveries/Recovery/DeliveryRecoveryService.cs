using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Domain.Auditing;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Recovery
{
    public sealed class DeliveryRecoveryService : IDeliveryRecoveryService
    {
        public const int MaxPageSize = 100;
        public const int MaxReplayBatchLimit = 5000;

        private readonly IDeliveryRepository _deliveries;
        private readonly IAuditLogRepository _auditLogs;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppLogger<DeliveryRecoveryService> _logger;

        public DeliveryRecoveryService(
            IDeliveryRepository deliveries,
            IAuditLogRepository auditLogs,
            IUnitOfWork unitOfWork,
            IAppLogger<DeliveryRecoveryService> logger)
        {
            _deliveries = deliveries ?? throw new ArgumentNullException(nameof(deliveries));
            _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
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

        public async Task<Result<DeliveryReplayJobView>> SubmitReplayJobAsync(
            DeliveryReplayRequest request,
            CancellationToken cancellationToken)
        {
            if (request.OperationId == Guid.Empty)
                return Result.Failure<DeliveryReplayJobView>(Error.Validation("operationId is required."));
            if (string.IsNullOrWhiteSpace(request.OperatorId))
                return Result.Failure<DeliveryReplayJobView>(Error.Validation("operatorId is required."));
            if (string.IsNullOrWhiteSpace(request.RequestSnapshot))
                return Result.Failure<DeliveryReplayJobView>(Error.Validation("requestSnapshot is required."));
            if (request.BatchLimit < 1 || request.BatchLimit > MaxReplayBatchLimit)
            {
                return Result.Failure<DeliveryReplayJobView>(
                    Error.Validation($"batchLimit must be between 1 and {MaxReplayBatchLimit}."));
            }

            var eventType = Normalize(request.EventType);
            var customerId = Normalize(request.CustomerId);
            var contractId = request.ContractId;
            var createdFrom = request.CreatedFrom;
            var createdTo = request.CreatedTo;

            if (createdFrom.HasValue && createdTo.HasValue && createdFrom > createdTo)
            {
                return Result.Failure<DeliveryReplayJobView>(
                    Error.Validation("createdFrom must be less than or equal to createdTo."));
            }

            if (eventType is null
                && customerId is null
                && !contractId.HasValue
                && !createdFrom.HasValue
                && !createdTo.HasValue)
            {
                return Result.Failure<DeliveryReplayJobView>(
                    Error.Validation("At least one replay filter must be provided."));
            }

            var existing = await _deliveries.GetReplayJobByOperationIdAsync(request.OperationId, cancellationToken);
            if (existing.IsFailure)
                return Result<DeliveryReplayJobView>.Fail(existing.Errors);

            if (existing.Value is not null)
                return Result.Success(ToView(existing.Value));

            try
            {
                var now = DateTimeOffset.UtcNow;
                var job = DeliveryReplayJob.Create(
                    operationId: request.OperationId,
                    operatorId: request.OperatorId.Trim(),
                    requestSnapshot: request.RequestSnapshot,
                    eventType: eventType,
                    customerId: customerId,
                    contractId: contractId,
                    createdFrom: createdFrom,
                    createdTo: createdTo,
                    batchLimit: request.BatchLimit,
                    now: now);

                _unitOfWork.Begin(true);
                var inserted = await _deliveries.AddReplayJobAsync(job, _unitOfWork, cancellationToken);
                if (inserted.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<DeliveryReplayJobView>.Fail(inserted.Errors);
                }

                if (inserted.Value == 0)
                {
                    _unitOfWork.Rollback();
                    var winner = await _deliveries.GetReplayJobByOperationIdAsync(request.OperationId, cancellationToken);
                    if (winner.IsFailure)
                        return Result<DeliveryReplayJobView>.Fail(winner.Errors);

                    if (winner.Value is not null)
                        return Result.Success(ToView(winner.Value));

                    return Result.Failure<DeliveryReplayJobView>(Error.Conflict("Replay job already exists."));
                }

                var audit = AuditLogEntry.Create(
                    category: "delivery",
                    action: "replay_requested",
                    actorId: request.OperatorId,
                    customerId: request.CustomerId,
                    contractId: request.ContractId,
                    deliveryId: null,
                    eventId: null,
                    operationId: request.OperationId,
                    details: request.RequestSnapshot,
                    createdAt: now,
                    expiresAt: now.AddDays(90));
                var auditInsert = await _auditLogs.AddAsync(audit, _unitOfWork, cancellationToken);
                if (auditInsert.IsFailure)
                {
                    _unitOfWork.Rollback();
                    return Result<DeliveryReplayJobView>.Fail(auditInsert.Errors);
                }

                _unitOfWork.Commit();
                return Result.Success(ToView(job));
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.Error(ex, "Failed to submit delivery replay job for operation {OperationId}.", request.OperationId);
                return Result.Failure<DeliveryReplayJobView>(ex);
            }
        }

        public async Task<Result<DeliveryReplayJobView>> GetReplayJobAsync(
            Guid replayJobId,
            CancellationToken cancellationToken)
        {
            if (replayJobId == Guid.Empty)
                return Result.Failure<DeliveryReplayJobView>(Error.Validation("replayJobId is required."));

            var lookup = await _deliveries.GetReplayJobByIdAsync(replayJobId, cancellationToken);
            if (lookup.IsFailure)
                return Result<DeliveryReplayJobView>.Fail(lookup.Errors);

            if (lookup.Value is null)
                return Result.Failure<DeliveryReplayJobView>(Error.NotFound($"Replay job '{replayJobId}' was not found."));

            return Result.Success(ToView(lookup.Value));
        }

        public async Task<Result<DeliveryReplayWorkerResult>> ProcessReplayJobsAsync(
            int maxJobs,
            CancellationToken cancellationToken)
        {
            if (maxJobs <= 0)
            {
                return Result.Failure<DeliveryReplayWorkerResult>(
                    Error.Validation("maxJobs must be greater than zero."));
            }

            var claim = await _deliveries.ClaimReplayJobsAsync(maxJobs, cancellationToken);
            if (claim.IsFailure)
                return Result<DeliveryReplayWorkerResult>.Fail(claim.Errors);

            var claimedJobs = claim.Value;
            var completed = 0;
            var failed = 0;
            var replayed = 0;

            foreach (var job in claimedJobs)
            {
                try
                {
                    var replay = await _deliveries.ExecuteReplayJobAsync(job, cancellationToken);
                    if (replay.IsFailure)
                    {
                        failed++;
                        await _deliveries.CompleteReplayJobAsync(
                            job.Id,
                            DeliveryReplayJobStatus.Failed,
                            replayedCount: 0,
                            error: replay.Error?.Message ?? "Replay failed.",
                            cancellationToken);
                        continue;
                    }

                    var completion = await _deliveries.CompleteReplayJobAsync(
                        job.Id,
                        DeliveryReplayJobStatus.Completed,
                        replay.Value,
                        error: null,
                        cancellationToken);

                    if (completion.IsFailure)
                    {
                        failed++;
                        _logger.Warning(
                            "Failed to complete replay job {ReplayJobId}: {Error}.",
                            job.Id,
                            completion.Error?.Message ?? "unknown");
                        continue;
                    }

                    completed++;
                    replayed += replay.Value;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.Error(ex, "Unhandled error processing replay job {ReplayJobId}.", job.Id);

                    var markFailed = await _deliveries.CompleteReplayJobAsync(
                        job.Id,
                        DeliveryReplayJobStatus.Failed,
                        replayedCount: 0,
                        error: ex.Message,
                        cancellationToken);

                    if (markFailed.IsFailure)
                    {
                        _logger.Warning(
                            "Failed to mark replay job {ReplayJobId} as failed after exception.",
                            job.Id);
                    }
                }
            }

            return Result.Success(new DeliveryReplayWorkerResult(
                claimedJobs.Count,
                completed,
                failed,
                replayed));
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

                    var operationAudit = AuditLogEntry.Create(
                        category: "delivery",
                        action: operation,
                        actorId: request.OperatorId,
                        customerId: null,
                        contractId: null,
                        deliveryId: deliveryId,
                        eventId: null,
                        operationId: request.OperationId,
                        details: request.RequestSnapshot,
                        createdAt: now,
                        expiresAt: now.AddDays(90));
                    var operationAuditInsert = await _auditLogs.AddAsync(operationAudit, _unitOfWork, cancellationToken);
                    if (operationAuditInsert.IsFailure)
                    {
                        _unitOfWork.Rollback();
                        return Result<DeliveryRecoveryView>.Fail(operationAuditInsert.Errors);
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

        private static DeliveryReplayJobView ToView(DeliveryReplayJob replayJob) =>
            new(
                replayJob.Id,
                replayJob.OperationId,
                replayJob.OperatorId,
                replayJob.Status,
                replayJob.EventType,
                replayJob.CustomerId,
                replayJob.ContractId,
                replayJob.CreatedFrom,
                replayJob.CreatedTo,
                replayJob.BatchLimit,
                replayJob.ReplayedCount,
                replayJob.Error,
                replayJob.CreatedAt,
                replayJob.UpdatedAt,
                replayJob.CompletedAt);

        private static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }
    }
}
