using KairoServer.Domain.Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Domain.Events
{
    public sealed class Event
    {
        public Guid Id { get; }
        public string IdempotencyKey { get; }
        public string Type { get; }
        /// <summary>
        /// Canonical identifier of the originating <see cref="Sources.Source"/>.
        /// This value is preserved verbatim from ingestion onto the canonical Event and
        /// must never be replaced with, or confused with, a destination identifier.
        /// It corresponds to the normalized value produced by <see cref="Sources.SourceId"/>.
        /// </summary>
        public string SourceId { get; }
        public string Payload { get; }
        public DateTimeOffset OccurredAt { get; }
        public DateTimeOffset ReceivedAt { get; }
        public EventMetadata? Metadata { get; }

        private Event(
            Guid id,
            string idempotencyKey,
            string type,
            string sourceId,
            string payload,
            DateTimeOffset occurredAt,
            DateTimeOffset receivedAt,
            EventMetadata? metadata)
        {
            Id = id;
            IdempotencyKey = idempotencyKey;
            Type = type;
            SourceId = sourceId;
            OccurredAt = occurredAt;
            Payload = payload;
            Metadata = metadata;
            ReceivedAt = receivedAt;
        }

        public static Event Create(
            string idempotencyKey,
            string type,
            string sourceId,
            DateTimeOffset occurredAt,
            string payload,
            EventMetadata? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("Event type is required.");

            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Event source is required.");

            return new Event(
                Guid.NewGuid(),
                idempotencyKey,
                type,
                sourceId,
                payload,
                occurredAt,
                DateTimeOffset.UtcNow,
                metadata);
        }

        public static Event Create(
            string idempotencyKey,
            string type,
            string sourceId,
            DateTimeOffset occurredAt,
            string payload,
            string? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("Event type is required.");

            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Event source is required.");

            var eventMetadata = metadata != null ? EventMetadata.Parse(metadata) : null;

            return new Event(
                Guid.NewGuid(),
                idempotencyKey,
                type,
                sourceId,
                payload,
                occurredAt,
                DateTimeOffset.UtcNow,
                eventMetadata);
        }

        /// <summary>
        /// Rehydrates an <see cref="Event"/> from persistence. Bypasses invariant
        /// enforcement on the assumption the stored row is already valid, and
        /// preserves the authoritative <paramref name="id"/> and <paramref name="receivedAt"/>.
        /// </summary>
        public static Event Rehydrate(
            Guid id,
            string idempotencyKey,
            string type,
            string sourceId,
            DateTimeOffset occurredAt,
            DateTimeOffset receivedAt,
            string payload,
            EventMetadata? metadata) =>
            new(id, idempotencyKey, type, sourceId, payload, occurredAt, receivedAt, metadata);
    }
}
