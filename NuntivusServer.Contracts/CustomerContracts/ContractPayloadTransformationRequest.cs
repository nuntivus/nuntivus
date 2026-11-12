using System.Collections.Generic;
using System.Text.Json;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadTransformationRequest(
        int Version,
        IReadOnlyCollection<ContractPayloadFieldMappingRequest> FieldMappings,
        JsonElement? Expression = null);
}
