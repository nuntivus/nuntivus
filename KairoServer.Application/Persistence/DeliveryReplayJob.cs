using System;
using KairoServer.Application.Deliveries.Recovery;

namespace KairoServer.Application.Persistence
{
    public sealed class DeliveryReplayJob
    {
        private DeliveryReplayJob(
            Guid id,
            Guid operationId,
            string operatorId,
            string requestSnapshot,
            DeliveryReplayJobStatus status,
            string? eventType,
            string? customerId,
            Guid? contractId,
            DateTimeOffset? createdFrom,
            DateTimeOffset? createdTo,
            int batchLimit,
            int replayedCount,
            string? error,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            DateTimeOffset? completedAt)
        {
            Id = id;
            OperationId = operationId;
            OperatorId = operatorId;
            RequestSnapshot = requestSnapshot;
            Status = status;
            EventType = eventType;
            CustomerId = customerId;
            ContractId = contractId;
            CreatedFrom = createdFrom;
            CreatedTo = createdTo;
            BatchLimit = batchLimit;
            ReplayedCount = replayedCount;
            Error = error;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            CompletedAt = completedAt;
        }

        public Guid Id { get; }
        public Guid OperationId { get; }
        public string OperatorId { get; }
        public string RequestSnapshot { get; }
        public DeliveryReplayJobStatus Status { get; }
        public string? EventType { get; }
        public string? CustomerId { get; }
        public Guid? ContractId { get; }
        public DateTimeOffset? CreatedFrom { get; }
        public DateTimeOffset? CreatedTo { get; }
        public int BatchLimit { get; }
        public int ReplayedCount { get; }
        public string? Error { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; }
        public DateTimeOffset? CompletedAt { get; }

        public static DeliveryReplayJob Create(
            Guid operationId,
            string operatorId,
            string requestSnapshot,
            string? eventType,
            string? customerId,
            Guid? contractId,
            DateTimeOffset? createdFrom,
            DateTimeOffset? createdTo,
            int batchLimit,
            DateTimeOffset now)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(requestSnapshot);

            return new DeliveryReplayJob(
                id: Guid.NewGuid(),
                operationId: operationId,
                operatorId: operatorId,
                requestSnapshot: requestSnapshot,
                status: DeliveryReplayJobStatus.Pending,
                eventType: eventType,
                customerId: customerId,
                contractId: contractId,
                createdFrom: createdFrom,
                createdTo: createdTo,
                batchLimit: batchLimit,
                replayedCount: 0,
                error: null,
                createdAt: now,
                updatedAt: now,
                completedAt: null);
        }

        public static DeliveryReplayJob Rehydrate(
            Guid id,
            Guid operationId,
            string operatorId,
            string requestSnapshot,
            DeliveryReplayJobStatus status,
            string? eventType,
            string? customerId,
            Guid? contractId,
            DateTimeOffset? createdFrom,
            DateTimeOffset? createdTo,
            int batchLimit,
            int replayedCount,
            string? error,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            DateTimeOffset? completedAt) =>
            new(
                id,
                operationId,
                operatorId,
                requestSnapshot,
                status,
                eventType,
                customerId,
                contractId,
                createdFrom,
                createdTo,
                batchLimit,
                replayedCount,
                error,
                createdAt,
                updatedAt,
                completedAt);
    }
}
