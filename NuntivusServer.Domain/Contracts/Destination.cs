using System;

namespace NuntivusServer.Domain.Contracts
{
    /// <summary>
    /// Value object describing where and how a Contract delivers events.
    /// <see cref="Configuration"/> is an opaque JSON payload whose schema is
    /// determined by <see cref="Type"/> and validated in the application layer.
    /// </summary>
    public sealed class Destination
    {
        public DestinationType Type { get; }
        public string Configuration { get; }

        public Destination(DestinationType type, string configuration)
        {
            if (string.IsNullOrWhiteSpace(configuration))
                throw new ArgumentException(
                    "Destination configuration cannot be null or whitespace.",
                    nameof(configuration));

            Type = type;
            Configuration = configuration;
        }
    }
}
