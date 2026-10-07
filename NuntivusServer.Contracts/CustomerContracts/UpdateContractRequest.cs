using System.Collections.Generic;
using System;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record UpdateContractRequest(
        string? Name,
        IReadOnlyCollection<string> EventTypes,
        DestinationConfig? Configuration = null,
        IReadOnlyCollection<ContractSchemaTargetRequest>? SchemaTargets = null,
        ContractFilterRequest? Filter = null,
        ContractPayloadTransformationRequest? Transformation = null,
        ContractRateLimitConfig? RateLimit = null,
        ContractOrderingConfig? Ordering = null,
        IReadOnlyCollection<Guid>? SourceIds = null,
        IReadOnlyCollection<Guid>? DestinationIds = null,
        IReadOnlyCollection<Guid>? RefreshDestinationIds = null,
        IReadOnlyCollection<Guid>? RemoveDestinationBindingIds = null);
}
