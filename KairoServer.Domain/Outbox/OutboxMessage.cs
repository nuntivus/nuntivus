using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Domain.Outbox
{
    public sealed class OutboxMessage
    {
        public Guid Id { get; }
        public string Type { get; }
        public string Payload { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? ProcessedAt { get; private set; }
        public int AttemptCount { get; private set; }

        private OutboxMessage(
            Guid id,
            string type,
            string payload,
            DateTimeOffset createdAt)
        {
            Id = id;
            Type = type;
            Payload = payload;
            CreatedAt = createdAt;
        }

        public static OutboxMessage Create(
            string type,
            string payload,
            DateTimeOffset createdAt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            ArgumentException.ThrowIfNullOrWhiteSpace(payload);

            return new OutboxMessage(
                Guid.NewGuid(),
                type,
                payload,
                createdAt);
        }
    }
}
