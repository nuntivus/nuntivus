using System.Collections.Generic;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record UpdateContractRequest(
        string? Name,
        IReadOnlyCollection<string> EventTypes,
        DestinationConfig Configuration,
        IReadOnlyCollection<ContractSchemaTargetRequest>? SchemaTargets = null,
        ContractFilterRequest? Filter = null,
        ContractPayloadTransformationRequest? Transformation = null,
        ContractRateLimitConfig? RateLimit = null,
        ContractOrderingConfig? Ordering = null);
}
