using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    /// <summary>
    /// Payload for <c>POST /v1/contracts</c>. The destination type is derived
    /// from the concrete type of <see cref="Configuration"/> (via the
    /// <c>"type"</c> JSON discriminator on <see cref="DestinationConfig"/>).
    /// </summary>
    public sealed record CreateContractRequest(
        string CustomerId,
        IReadOnlyCollection<string> EventTypes,
        DestinationConfig Configuration,
        IReadOnlyCollection<ContractSchemaTargetRequest>? SchemaTargets = null,
    ContractFilterRequest? Filter = null,
    ContractPayloadTransformationRequest? Transformation = null);
}
