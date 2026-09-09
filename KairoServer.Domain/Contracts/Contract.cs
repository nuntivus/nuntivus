using System;
using System.Collections.Generic;
using System.Linq;

namespace KairoServer.Domain.Contracts
{
    /// <summary>
    /// Configuration describing how a specific <c>Customer</c> consumes a set of
    /// event types via a configured <see cref="Destination"/>.
    /// A Contract is the unit the matching pipeline evaluates to decide whether
    /// an inbound event must be delivered to a Customer.
    /// </summary>
    public sealed class Contract
    {
        private readonly HashSet<string> _eventTypes;

        public Guid Id { get; }
        public string CustomerId { get; }
        public bool Enabled { get; private set; }
        public Destination Destination { get; private set; }
        public IReadOnlyCollection<string> EventTypes => _eventTypes;
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        private Contract(
            Guid id,
            string customerId,
            bool enabled,
            IEnumerable<string> eventTypes,
            Destination destination,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt)
        {
            Id = id;
            CustomerId = customerId;
            Enabled = enabled;
            _eventTypes = new HashSet<string>(eventTypes, StringComparer.Ordinal);
            Destination = destination;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public static Contract Create(
            string customerId,
            IEnumerable<string> eventTypes,
            Destination destination,
            DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(customerId))
                throw new ArgumentException("Customer id cannot be null or whitespace.", nameof(customerId));

            if (destination is null)
                throw new ArgumentNullException(nameof(destination));

            if (eventTypes is null)
                throw new ArgumentNullException(nameof(eventTypes));

            var materialized = eventTypes
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .ToArray();

            if (materialized.Length == 0)
                throw new ArgumentException("At least one event type is required.", nameof(eventTypes));

            return new Contract(
                Guid.NewGuid(),
                customerId.Trim(),
                enabled: true,
                materialized,
                destination,
                createdAt: now,
                updatedAt: now);
        }

        /// <summary>
        /// Rehydrates a Contract from persistence. Bypasses invariant enforcement
        /// on the assumption the stored row is already valid.
        /// </summary>
        public static Contract Rehydrate(
            Guid id,
            string customerId,
            bool enabled,
            IEnumerable<string> eventTypes,
            Destination destination,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt) =>
            new(id, customerId, enabled, eventTypes, destination, createdAt, updatedAt);

        public void Enable(DateTimeOffset now)
        {
            if (Enabled) return;
            Enabled = true;
            UpdatedAt = now;
        }

        public void Disable(DateTimeOffset now)
        {
            if (!Enabled) return;
            Enabled = false;
            UpdatedAt = now;
        }
    }
}
