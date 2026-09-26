using System;

namespace NuntivusServer.Domain.Customers
{
    /// <summary>
    /// Represents an external organization, tenant, or partner that consumes events from Nuntivus.
    /// A Customer is the ownership boundary for future Sources, Subscribers, and Deliveries.
    /// </summary>
    public sealed class Customer
    {
        public string Id { get; }
        public string Name { get; private set; }
        public bool Enabled { get; private set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        private Customer(
            string id,
            string name,
            bool enabled,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt)
        {
            Id = id;
            Name = name;
            Enabled = enabled;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public static Customer Create(
            string id,
            string name,
            DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Customer id cannot be null or whitespace.", nameof(id));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Customer name cannot be null or whitespace.", nameof(name));

            return new Customer(id.Trim(), name.Trim(), enabled: true, createdAt: now, updatedAt: now);
        }

        /// <summary>
        /// Rehydrates a Customer from persistence. Bypasses invariant enforcement
        /// on the assumption the stored row is already valid.
        /// </summary>
        public static Customer Rehydrate(
            string id,
            string name,
            bool enabled,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt) =>
            new(id, name, enabled, createdAt, updatedAt);

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

        public void Rename(string name, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Customer name cannot be null or whitespace.", nameof(name));

            var normalized = name.Trim();
            if (string.Equals(Name, normalized, StringComparison.Ordinal))
                return;

            Name = normalized;
            UpdatedAt = now;
        }
    }
}
