using System;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.Application.Contracts.Destinations
{
    /// <summary>
    /// Validates and materializes a specific <see cref="DestinationConfig"/> variant
    /// into a persistable domain <see cref="Destination"/>.
    /// Each concrete destination configuration type has exactly one handler.
    /// </summary>
    public interface IDestinationConfigHandler
    {
        /// <summary>
        /// The concrete <see cref="DestinationConfig"/>-derived type this handler supports.
        /// </summary>
        Type ConfigType { get; }

        /// <summary>
        /// The domain destination type emitted by this handler.
        /// </summary>
        DomainDestinationType DestinationType { get; }

        /// <summary>
        /// Validates <paramref name="configuration"/> and returns a domain <see cref="Destination"/>
        /// with a canonical JSON payload suitable for persistence.
        /// </summary>
        Result<Destination> Build(DestinationConfig configuration);

        /// <summary>
        /// Redacts sensitive values from a persisted destination configuration JSON payload.
        /// </summary>
        Result<string> Redact(string configurationJson);
    }
}
