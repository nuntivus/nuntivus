using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuntivusServer.Domain.Outbox
{
    public sealed class OutboxMessage
    {
        public Guid Id { get; }
        public Guid EventId {  get; }
        public string Type { get; }
        public string Payload { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? ProcessedAt { get; private set; }
        public int AttemptCount { get; private set; }
        public DateTimeOffset? LastFailedAt { get; private set; }
        public DateTimeOffset? NextAttemptAt { get; private set; }
        public string? LastError { get; private set; }

        private OutboxMessage(
            Guid id,
            Guid eventId,
            string type,
            string payload,
            DateTimeOffset createdAt)
        {
            Id = id;
            EventId = eventId;
            Type = type;
            Payload = payload;
            CreatedAt = createdAt;
        }

        public static OutboxMessage Create(
            Guid eventId,
            string type,
            string payload,
            DateTimeOffset createdAt)
        {
            if (eventId == Guid.Empty) 
                throw new ArgumentException("EventId cannot be empty.", nameof(eventId));
            
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            ArgumentException.ThrowIfNullOrWhiteSpace(payload);

            return new OutboxMessage(
                Guid.NewGuid(),
                eventId,
                type,
                payload,
                createdAt);
        }

        /// <summary>
        /// Rehydrates an <see cref="OutboxMessage"/> from persistence.
        /// </summary>
        public static OutboxMessage Restore(
            Guid id,
            Guid eventId,
            string type,
            string payload,
            DateTimeOffset createdAt,
            DateTimeOffset? processedAt,
            int attemptCount,
            DateTimeOffset? lastFailedAt = null,
            DateTimeOffset? nextAttemptAt = null,
            string? lastError = null)
        {
            return new OutboxMessage(id, eventId, type, payload, createdAt)
            {
                ProcessedAt = processedAt,
                AttemptCount = attemptCount,
                LastFailedAt = lastFailedAt,
                NextAttemptAt = nextAttemptAt,
                LastError = lastError,
            };
        }
    }
}
