using System;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Domain.Deliveries
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
        public Guid DestinationBindingId { get; }
        public string CustomerId { get; }
        public string EventType { get; }
        public string Payload { get; }
        public string? AuthenticationReference { get; }
        public int? AuthenticationVersion { get; }
        public int? TransformationVersion { get; }
        public int? SchemaVersion { get; }
        public string? TransformationConfiguration { get; }
        public ContractRateLimit? RateLimit { get; }
        public bool OrderingEnabled { get; }
        public string? OrderingKey { get; }
        public DestinationType DestinationType { get; }
        public string DestinationConfiguration { get; }
        public DeliveryStatus Status { get; private set; }
        public int AttemptCount { get; private set; }
        public int AttemptNumberOffset { get; private set; }
        public int AttemptNumber => AttemptNumberOffset + AttemptCount;
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
            string? authenticationReference,
            int? authenticationVersion,
            int? transformationVersion,
            int? schemaVersion,
            string? transformationConfiguration,
            ContractRateLimit? rateLimit,
            bool orderingEnabled,
            string? orderingKey,
            DestinationType destinationType,
            string destinationConfiguration,
            DeliveryStatus status,
            int attemptCount,
            string? lastError,
            DateTimeOffset? nextAttemptAt,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            Guid destinationBindingId = default,
            int attemptNumberOffset = 0)
        {
            Id = id;
            EventId = eventId;
            ContractId = contractId;
            DestinationBindingId = destinationBindingId;
            CustomerId = customerId;
            EventType = eventType;
            Payload = payload;
            AuthenticationReference = authenticationReference;
            AuthenticationVersion = authenticationVersion;
            TransformationVersion = transformationVersion;
            SchemaVersion = schemaVersion;
            TransformationConfiguration = transformationConfiguration;
            RateLimit = rateLimit;
            OrderingEnabled = orderingEnabled;
            OrderingKey = orderingKey;
            DestinationType = destinationType;
            DestinationConfiguration = destinationConfiguration;
            Status = status;
            AttemptCount = attemptCount;
            AttemptNumberOffset = attemptNumberOffset;
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
            ContractRateLimit? rateLimit = null,
            ContractOrdering? ordering = null,
            string? authenticationReference = null,
            int? authenticationVersion = null,
            int? transformationVersion = null,
            int? schemaVersion = null,
            Guid destinationBindingId = default)
        {
            if (eventId == Guid.Empty)
                throw new ArgumentException("EventId cannot be empty.", nameof(eventId));
            if (contractId == Guid.Empty)
                throw new ArgumentException("ContractId cannot be empty.", nameof(contractId));

            ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
            ArgumentException.ThrowIfNullOrWhiteSpace(payload);
            ArgumentNullException.ThrowIfNull(destination);

            var orderingKey = ordering is null ? null : ResolveOrderingKey(contractId, ordering);

            return new Delivery(
                UuidV7.Create(now),
                eventId,
                contractId,
                customerId,
                eventType,
                payload,
                authenticationReference,
                authenticationVersion,
                transformationVersion,
                schemaVersion,
                transformationConfiguration,
                rateLimit,
                ordering is not null,
                orderingKey,
                destination.Type,
                destination.Configuration,
                DeliveryStatus.Pending,
                attemptCount: 0,
                lastError: null,
                nextAttemptAt: null,
                createdAt: now,
                updatedAt: now,
                destinationBindingId: destinationBindingId);
        }

        private static string ResolveOrderingKey(Guid contractId, ContractOrdering ordering)
        {
            ArgumentNullException.ThrowIfNull(ordering);

            if (contractId == Guid.Empty)
                throw new ArgumentException("ContractId cannot be empty.", nameof(contractId));

            return $"contract:{contractId:D}";
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
        /// Manual retry operation: reactivates a dead or failed delivery by resetting
        /// attempt count and scheduling immediate dispatch. An unscheduled pending
        /// delivery may also be explicitly rescheduled.
        /// </summary>
        public bool ManualRetryReset(DateTimeOffset now)
        {
            if (Status == DeliveryStatus.Pending && NextAttemptAt is not null)
                return false;

            if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Dead or DeliveryStatus.Failed))
                throw new InvalidOperationException(
                    $"Cannot manually retry delivery from {Status}.");

            Status = DeliveryStatus.Pending;
            AttemptNumberOffset += AttemptCount;
            AttemptCount = 0;
            LastError = null;
            NextAttemptAt = now;
            UpdatedAt = now;
            return true;
        }

        /// <summary>
        /// Manual requeue operation: reactivates a dead or failed delivery preserving
        /// attempt count and scheduling immediate retry. Returns false when already pending.
        /// </summary>
        public bool ManualRequeue(DateTimeOffset now)
        {
            if (Status == DeliveryStatus.Pending)
                return false;

            if (Status is not (DeliveryStatus.Dead or DeliveryStatus.Failed))
                throw new InvalidOperationException(
                    $"Cannot manually requeue delivery from {Status}.");

            Status = DeliveryStatus.Failed;
            NextAttemptAt = now;
            UpdatedAt = now;
            return true;
        }

        /// <summary>
        /// Manual cancel operation: transitions pending/failed/dead delivery into
        /// terminal dead state with an operator-provided reason. Returns false when
        /// already dead.
        /// </summary>
        public bool ManualCancel(string reason, DateTimeOffset now)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Failed or DeliveryStatus.Dead))
                throw new InvalidOperationException(
                    $"Cannot manually cancel delivery from {Status}.");

            if (Status == DeliveryStatus.Dead)
                return false;

            Status = DeliveryStatus.Dead;
            LastError = reason;
            NextAttemptAt = null;
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
            string? authenticationReference,
            int? authenticationVersion,
            int? transformationVersion,
            int? schemaVersion,
            string? transformationConfiguration,
            ContractRateLimit? rateLimit,
            bool orderingEnabled,
            string? orderingKey,
            DestinationType destinationType,
            string destinationConfiguration,
            DeliveryStatus status,
            int attemptCount,
            string? lastError,
            DateTimeOffset? nextAttemptAt,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            Guid destinationBindingId = default,
            int attemptNumberOffset = 0) =>
            new(id, eventId, contractId, customerId, eventType, payload,
                authenticationReference,
                authenticationVersion,
                transformationVersion,
                schemaVersion,
                transformationConfiguration, rateLimit, orderingEnabled, orderingKey, destinationType, destinationConfiguration, status,
                attemptCount, lastError, nextAttemptAt, createdAt, updatedAt, destinationBindingId, attemptNumberOffset);

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
                authenticationReference: null,
                authenticationVersion: null,
                transformationVersion: null,
                schemaVersion: null,
                transformationConfiguration: null,
                rateLimit: null,
                orderingEnabled: false,
                orderingKey: null,
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
