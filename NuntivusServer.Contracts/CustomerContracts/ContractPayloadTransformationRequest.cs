using System.Collections.Generic;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadTransformationRequest(
        int Version,
        IReadOnlyCollection<ContractPayloadFieldMappingRequest> FieldMappings);
}
