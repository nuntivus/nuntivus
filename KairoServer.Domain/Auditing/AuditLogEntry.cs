using System;

namespace KairoServer.Domain.Auditing
{
    public sealed class AuditLogEntry
    {
        private AuditLogEntry(
            Guid id,
            string category,
            string action,
            string? actorId,
            string? customerId,
            Guid? contractId,
            Guid? deliveryId,
            Guid? eventId,
            Guid? operationId,
            string details,
            DateTimeOffset createdAt,
            DateTimeOffset expiresAt)
        {
            Id = id;
            Category = category;
            Action = action;
            ActorId = actorId;
            CustomerId = customerId;
            ContractId = contractId;
            DeliveryId = deliveryId;
            EventId = eventId;
            OperationId = operationId;
            Details = details;
            CreatedAt = createdAt;
            ExpiresAt = expiresAt;
        }

        public Guid Id { get; }
        public string Category { get; }
        public string Action { get; }
        public string? ActorId { get; }
        public string? CustomerId { get; }
        public Guid? ContractId { get; }
        public Guid? DeliveryId { get; }
        public Guid? EventId { get; }
        public Guid? OperationId { get; }
        public string Details { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset ExpiresAt { get; }

        public static AuditLogEntry Create(
            string category,
            string action,
            string? actorId,
            string? customerId,
            Guid? contractId,
            Guid? deliveryId,
            Guid? eventId,
            Guid? operationId,
            string details,
            DateTimeOffset createdAt,
            DateTimeOffset expiresAt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentException.ThrowIfNullOrWhiteSpace(details);
            if (expiresAt <= createdAt)
                throw new ArgumentOutOfRangeException(nameof(expiresAt), "ExpiresAt must be greater than CreatedAt.");

            return new AuditLogEntry(
                Guid.NewGuid(),
                category,
                action,
                string.IsNullOrWhiteSpace(actorId) ? null : actorId.Trim(),
                string.IsNullOrWhiteSpace(customerId) ? null : customerId.Trim(),
                contractId,
                deliveryId,
                eventId,
                operationId,
                details,
                createdAt,
                expiresAt);
        }
    }
}
