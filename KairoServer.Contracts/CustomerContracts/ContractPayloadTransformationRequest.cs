using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadTransformationRequest(
        int Version,
        IReadOnlyCollection<ContractPayloadFieldMappingRequest> FieldMappings);
}
