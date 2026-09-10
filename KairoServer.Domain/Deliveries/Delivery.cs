using System;

namespace KairoServer.Domain.Deliveries
{
    /// <summary>
    /// A logical delivery of an Event to a specific Contract.
    /// Created (idempotently) by the contract matcher stage; later stages
    /// drive it through <see cref="DeliveryStatus.InFlight"/> to a terminal state.
    /// The <see cref="EventType"/> and <see cref="Payload"/> are denormalized
    /// so the dispatcher can render/deliver without rejoining events.
    /// </summary>
    public sealed class Delivery
    {
        public Guid Id { get; }
        public Guid EventId { get; }
        public Guid ContractId { get; }
        public string CustomerId { get; }
        public string EventType { get; }
        public string Payload { get; }
        public DeliveryStatus Status { get; private set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        private Delivery(
            Guid id,
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            DeliveryStatus status,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt)
        {
            Id = id;
            EventId = eventId;
            ContractId = contractId;
            CustomerId = customerId;
            EventType = eventType;
            Payload = payload;
            Status = status;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public static Delivery CreatePending(
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            DateTimeOffset now)
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("EventId cannot be empty.", nameof(eventId));
            if (contractId == Guid.Empty)
                throw new ArgumentException("ContractId cannot be empty.", nameof(contractId));

            ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
            ArgumentException.ThrowIfNullOrWhiteSpace(payload);

            return new Delivery(
                Guid.NewGuid(),
                eventId,
                contractId,
                customerId,
                eventType,
                payload,
                DeliveryStatus.Pending,
                now,
                now);
        }

        /// <summary>
        /// Rehydrates a Delivery from persistence. Bypasses invariant enforcement
        /// on the assumption the stored row is already valid.
        /// </summary>
        public static Delivery Rehydrate(
            Guid id,
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            DeliveryStatus status,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt) =>
            new(id, eventId, contractId, customerId, eventType, payload, status, createdAt, updatedAt);
    }
}
