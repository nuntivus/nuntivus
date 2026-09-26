using System;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DomainDestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Application.Contracts.Destinations
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
        /// Shared endpoint security context used by policy evaluation.
        /// </summary>
        EndpointSecurityContext SecurityContext { get; }

        /// <summary>
        /// Validates <paramref name="configuration"/> and returns a domain <see cref="Destination"/>
        /// with a canonical JSON payload suitable for persistence.
        /// </summary>
        Result<Destination> Build(DestinationConfig configuration);

        /// <summary>
        /// Redacts sensitive values from a persisted destination configuration JSON payload.
        /// </summary>
        Result<string> Redact(string configurationJson);

        /// <summary>
        /// Applies destination-specific credential rotation behavior.
        /// Implementations may augment <paramref name="next"/> with overlap metadata.
        /// </summary>
        Result<Destination> Rotate(Destination current, Destination next);
    }
}
