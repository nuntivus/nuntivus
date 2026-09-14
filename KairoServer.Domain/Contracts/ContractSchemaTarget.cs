using System;

namespace KairoServer.Domain.Contracts
{
    public sealed record ContractSchemaTarget
    {
        public string EventType { get; }
        public int Version { get; }

        public ContractSchemaTarget(string eventType, int version)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Event type is required.", nameof(eventType));

            if (version < 1)
                throw new ArgumentException("Version must be greater than 0.", nameof(version));

            EventType = eventType.Trim();
            Version = version;
        }
    }
}