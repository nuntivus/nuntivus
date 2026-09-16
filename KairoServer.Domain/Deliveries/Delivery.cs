using System;
using KairoServer.Domain.Contracts;

namespace KairoServer.Domain.Deliveries
{
    /// <summary>
    /// A logical delivery of an Event to a specific Contract.
    /// Created (idempotently) by the contract matcher stage; later stages
    /// drive it through <see cref="DeliveryStatus.InFlight"/> to a terminal state.
    /// The <see cref="EventType"/>, <see cref="Payload"/>, <see cref="DestinationType"/>
    /// and <see cref="DestinationConfiguration"/> are denormalized so the dispatcher
    /// can render/deliver reliably without rejoining events or contracts, and so
    /// that later contract edits do not retroactively affect in-flight deliveries.
    /// </summary>
    public sealed class Delivery
    {
        public Guid Id { get; }
        public Guid EventId { get; }
        public Guid ContractId { get; }
        public string CustomerId { get; }
        public string EventType { get; }
        public string Payload { get; }
        public string? TransformationConfiguration { get; }
        public ContractRateLimit? RateLimit { get; }
        public DestinationType DestinationType { get; }
        public string DestinationConfiguration { get; }
        public DeliveryStatus Status { get; private set; }
        public int AttemptCount { get; private set; }
        public string? LastError { get; private set; }
        public DateTimeOffset? NextAttemptAt { get; private set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        private Delivery(
            Guid id,
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            string? transformationConfiguration,
            ContractRateLimit? rateLimit,
            DestinationType destinationType,
            string destinationConfiguration,
            DeliveryStatus status,
            int attemptCount,
            string? lastError,
            DateTimeOffset? nextAttemptAt,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt)
        {
            Id = id;
            EventId = eventId;
            ContractId = contractId;
            CustomerId = customerId;
            EventType = eventType;
            Payload = payload;
            TransformationConfiguration = transformationConfiguration;
            RateLimit = rateLimit;
            DestinationType = destinationType;
            DestinationConfiguration = destinationConfiguration;
            Status = status;
            AttemptCount = attemptCount;
            LastError = lastError;
            NextAttemptAt = nextAttemptAt;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public static Delivery CreatePending(
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            Destination destination,
            DateTimeOffset now,
            string? transformationConfiguration = null,
            ContractRateLimit? rateLimit = null)
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("EventId cannot be empty.", nameof(eventId));
            if (contractId == Guid.Empty)
                throw new ArgumentException("ContractId cannot be empty.", nameof(contractId));

            ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
            ArgumentException.ThrowIfNullOrWhiteSpace(payload);
            ArgumentNullException.ThrowIfNull(destination);

            return new Delivery(
                UuidV7.Create(now),
                eventId,
                contractId,
                customerId,
                eventType,
                payload,
                transformationConfiguration,
                rateLimit,
                destination.Type,
                destination.Configuration,
                DeliveryStatus.Pending,
                attemptCount: 0,
                lastError: null,
                nextAttemptAt: null,
                createdAt: now,
                updatedAt: now);
        }

        /// <summary>
        /// Transitions to <see cref="DeliveryStatus.InFlight"/> and increments the attempt counter.
        /// Only valid from <see cref="DeliveryStatus.Pending"/> or <see cref="DeliveryStatus.Failed"/>
        /// (retry).
        /// </summary>
        public void MarkInFlight(DateTimeOffset now)
        {
            if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Failed))
                throw new InvalidOperationException(
                    $"Cannot transition to InFlight from {Status}.");

            Status = DeliveryStatus.InFlight;
            AttemptCount++;
            NextAttemptAt = null;
            UpdatedAt = now;
        }

        /// <summary>
        /// Transitions to the terminal <see cref="DeliveryStatus.Delivered"/> state.
        /// </summary>
        public void MarkDelivered(DateTimeOffset now)
        {
            if (Status != DeliveryStatus.InFlight)
                throw new InvalidOperationException(
                    $"Cannot transition to Delivered from {Status}.");

            Status = DeliveryStatus.Delivered;
            LastError = null;
            NextAttemptAt = null;
            UpdatedAt = now;
        }

        /// <summary>
        /// Records a failed attempt. When <paramref name="nextAttemptAt"/> is provided
        /// the delivery is eligible for retry.
        /// </summary>
        public void MarkFailed(string error, DateTimeOffset? nextAttemptAt, DateTimeOffset now)
        {
            if (Status != DeliveryStatus.InFlight)
                throw new InvalidOperationException(
                    $"Cannot transition to Failed from {Status}.");

            ArgumentException.ThrowIfNullOrWhiteSpace(error);

            Status = DeliveryStatus.Failed;
            LastError = error;
            NextAttemptAt = nextAttemptAt;
            UpdatedAt = now;
        }

        /// <summary>
        /// Transitions to terminal <see cref="DeliveryStatus.Dead"/> when retry policy is exhausted.
        /// </summary>
        public void MarkDead(string error, DateTimeOffset now)
        {
            if (Status is not (DeliveryStatus.InFlight or DeliveryStatus.Failed))
                throw new InvalidOperationException(
                    $"Cannot transition to Dead from {Status}.");

            ArgumentException.ThrowIfNullOrWhiteSpace(error);

            Status = DeliveryStatus.Dead;
            LastError = error;
            NextAttemptAt = null;
            UpdatedAt = now;
        }

        /// <summary>
        /// Manual retry operation: reactivates a dead delivery by resetting attempt count
        /// and clearing error/scheduling state. Returns false when already active.
        /// </summary>
        public bool ManualRetryReset(DateTimeOffset now)
        {
            if (Status != DeliveryStatus.Dead)
                return false;

            Status = DeliveryStatus.Pending;
            AttemptCount = 0;
            LastError = null;
            NextAttemptAt = null;
            UpdatedAt = now;
            return true;
        }

        /// <summary>
        /// Manual requeue operation: reactivates a dead delivery preserving attempt count.
        /// Returns false when already active.
        /// </summary>
        public bool ManualRequeue(DateTimeOffset now)
        {
            if (Status != DeliveryStatus.Dead)
                return false;

            Status = DeliveryStatus.Failed;
            NextAttemptAt = now;
            UpdatedAt = now;
            return true;
        }

        /// <summary>
        /// Rehydrates a Delivery from persistence. Bypasses invariant enforcement
        /// on the assumption the stored row (including transformation configuration snapshot) is already valid.
        /// </summary>
        public static Delivery Rehydrate(
            Guid id,
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            string? transformationConfiguration,
            ContractRateLimit? rateLimit,
            DestinationType destinationType,
            string destinationConfiguration,
            DeliveryStatus status,
            int attemptCount,
            string? lastError,
            DateTimeOffset? nextAttemptAt,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt) =>
            new(id, eventId, contractId, customerId, eventType, payload,
                transformationConfiguration, rateLimit, destinationType, destinationConfiguration, status,
                attemptCount, lastError, nextAttemptAt, createdAt, updatedAt);

        public static Delivery Rehydrate(
            Guid id,
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            DestinationType destinationType,
            string destinationConfiguration,
            DeliveryStatus status,
            int attemptCount,
            string? lastError,
            DateTimeOffset? nextAttemptAt,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt) =>
            Rehydrate(
                id,
                eventId,
                contractId,
                customerId,
                eventType,
                payload,
                transformationConfiguration: null,
                rateLimit: null,
                destinationType,
                destinationConfiguration,
                status,
                attemptCount,
                lastError,
                nextAttemptAt,
                createdAt,
                updatedAt);
    }
}
