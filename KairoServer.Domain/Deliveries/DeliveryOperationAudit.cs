using System;

namespace KairoServer.Domain.Deliveries
{
    public sealed class DeliveryOperationAudit
    {
        public Guid Id { get; }
        public Guid DeliveryId { get; }
        public Guid OperationId { get; }
        public string Operation { get; }
        public string OperatorId { get; }
        public string RequestSnapshot { get; }
        public string ResponseSnapshot { get; }
        public DateTimeOffset CreatedAt { get; }

        private DeliveryOperationAudit(
            Guid id,
            Guid deliveryId,
            Guid operationId,
            string operation,
            string operatorId,
            string requestSnapshot,
            string responseSnapshot,
            DateTimeOffset createdAt)
        {
            Id = id;
            DeliveryId = deliveryId;
            OperationId = operationId;
            Operation = operation;
            OperatorId = operatorId;
            RequestSnapshot = requestSnapshot;
            ResponseSnapshot = responseSnapshot;
            CreatedAt = createdAt;
        }

        public static DeliveryOperationAudit Create(
            Guid deliveryId,
            Guid operationId,
            string operation,
            string operatorId,
            string requestSnapshot,
            string responseSnapshot,
            DateTimeOffset now)
        {
            if (deliveryId == Guid.Empty)
                throw new ArgumentException("DeliveryId cannot be empty.", nameof(deliveryId));
            if (operationId == Guid.Empty)
                throw new ArgumentException("OperationId cannot be empty.", nameof(operationId));

            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(requestSnapshot);
            ArgumentException.ThrowIfNullOrWhiteSpace(responseSnapshot);

            return new DeliveryOperationAudit(
                UuidV7.Create(now),
                deliveryId,
                operationId,
                operation,
                operatorId,
                requestSnapshot,
                responseSnapshot,
                now);
        }

        public static DeliveryOperationAudit Rehydrate(
            Guid id,
            Guid deliveryId,
            Guid operationId,
            string operation,
            string operatorId,
            string requestSnapshot,
            string responseSnapshot,
            DateTimeOffset createdAt) =>
            new(id, deliveryId, operationId, operation, operatorId, requestSnapshot, responseSnapshot, createdAt);
    }
}
