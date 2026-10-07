using System;
using System.Collections.Generic;
using System.Linq;

namespace NuntivusServer.Domain.Contracts
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
        private readonly IReadOnlyCollection<Guid> _sourceIds;
        private readonly IReadOnlyCollection<ContractSchemaTarget> _schemaTargets;
        private IReadOnlyCollection<ContractDestinationBinding> _destinations;

        public Guid Id { get; }
        public string CustomerId { get; }
        public string? Name { get; }
        public bool Enabled { get; private set; }
        public Destination Destination { get; private set; }
        public IReadOnlyCollection<ContractDestinationBinding> Destinations => _destinations;
        public IReadOnlyCollection<string> EventTypes => _eventTypes;
        public IReadOnlyCollection<Guid> SourceIds => _sourceIds;
        public IReadOnlyCollection<ContractSchemaTarget> SchemaTargets => _schemaTargets;
        public ContractFilter? Filter { get; }
        public ContractPayloadTransformation? Transformation { get; }
        public ContractRateLimit? RateLimit { get; }
        public ContractOrdering? Ordering { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; private set; }

        private Contract(
            Guid id,
            string customerId,
            string? name,
            bool enabled,
            IEnumerable<string> eventTypes,
            IEnumerable<ContractSchemaTarget> schemaTargets,
            ContractFilter? filter,
            ContractPayloadTransformation? transformation,
            ContractRateLimit? rateLimit,
            ContractOrdering? ordering,
            Destination destination,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            IEnumerable<Guid> sourceIds,
            IEnumerable<ContractDestinationBinding>? destinations = null)
        {
            Id = id;
            CustomerId = customerId;
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            Enabled = enabled;
            _eventTypes = new HashSet<string>(eventTypes, StringComparer.Ordinal);
            _sourceIds = sourceIds.ToArray();
            _schemaTargets = new List<ContractSchemaTarget>(schemaTargets);
            Filter = filter is null
                ? null
                : new ContractFilter(filter.MetadataRules, filter.PayloadRules);
            Transformation = transformation is null
                ? null
                : new ContractPayloadTransformation(
                    transformation.Version,
                    transformation.FieldMappings);
            RateLimit = rateLimit;
            Ordering = ordering is null
                ? null
                : ContractOrdering.Create(ordering.KeySelector);
            Destination = destination;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            _destinations = destinations?.ToArray() ??
                [new ContractDestinationBinding(Guid.NewGuid(), null, destination, true, createdAt)];
            if (_destinations.Count > 0)
                Destination = _destinations.First().Destination;
        }

        public static Contract Create(
            string customerId,
            IEnumerable<string> eventTypes,
            IEnumerable<ContractSchemaTarget>? schemaTargets,
            ContractFilter? filter,
            Destination destination,
            DateTimeOffset now,
            ContractPayloadTransformation? transformation = null,
            ContractRateLimit? rateLimit = null,
            ContractOrdering? ordering = null,
            string? name = null,
            IEnumerable<Guid>? sourceIds = null)
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

            var materializedSchemaTargets = (schemaTargets ?? Array.Empty<ContractSchemaTarget>())
                .Select(t => new ContractSchemaTarget(t.EventType, t.Version))
                .ToArray();

            if (materialized.Length == 0)
                throw new ArgumentException("At least one event type is required.", nameof(eventTypes));

            var selectedSources = sourceIds?.ToArray() ?? [];
            if (selectedSources.Length == 0 || selectedSources.Any(id => id == Guid.Empty)
                || selectedSources.Distinct().Count() != selectedSources.Length)
                throw new ArgumentException("At least one unique, valid Source is required.", nameof(sourceIds));

            foreach (var target in materializedSchemaTargets)
            {
                if (!materialized.Contains(target.EventType, StringComparer.Ordinal))
                    throw new ArgumentException(
                        $"Schema target event type '{target.EventType}' must be present in contract event types.",
                        nameof(schemaTargets));
            }

            if (materializedSchemaTargets
                .GroupBy(t => t.EventType, StringComparer.Ordinal)
                .Any(g => g.Count() > 1))
            {
                throw new ArgumentException(
                    "Only one schema target per event type is allowed.",
                    nameof(schemaTargets));
            }

            return new Contract(
                Guid.NewGuid(),
                customerId.Trim(),
                name,
                enabled: true,
                materialized,
                materializedSchemaTargets,
                filter,
                transformation,
                rateLimit,
                ordering,
                destination,
                createdAt: now,
                updatedAt: now,
                selectedSources);
        }

        /// <summary>
        /// Rehydrates a Contract from persistence. Bypasses invariant enforcement
        /// on the assumption the stored row is already valid.
        /// </summary>
        public static Contract Rehydrate(
            Guid id,
            string customerId,
            string? name,
            bool enabled,
            IEnumerable<string> eventTypes,
            IEnumerable<ContractSchemaTarget>? schemaTargets,
            ContractFilter? filter,
            Destination destination,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            ContractPayloadTransformation? transformation = null,
            ContractRateLimit? rateLimit = null,
            ContractOrdering? ordering = null,
            IEnumerable<Guid>? sourceIds = null,
            IEnumerable<ContractDestinationBinding>? destinations = null) =>
            new(
                id,
                customerId,
                name,
                enabled,
                eventTypes,
                schemaTargets ?? Array.Empty<ContractSchemaTarget>(),
                filter,
                transformation,
                rateLimit,
                ordering,
                destination,
                createdAt,
                updatedAt,
                sourceIds ?? [],
                destinations);

        public void SetDestinations(IEnumerable<ContractDestinationBinding> destinations)
        {
            _destinations = destinations.ToArray();
            if (_destinations.Count > 0)
                Destination = _destinations.First().Destination;
        }

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

        public void UpdateDestination(Destination destination, DateTimeOffset now)
        {
            if (destination is null)
                throw new ArgumentNullException(nameof(destination));

            Destination = destination;
            UpdatedAt = now;
            _destinations = _destinations.Select((binding, index) => index == 0
                ? binding with { Destination = destination } : binding).ToArray();
        }
    }
}
