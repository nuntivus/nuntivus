using System.Collections.Generic;
using System.Text.Json;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadTransformationView(
        int Version,
        IReadOnlyCollection<ContractPayloadFieldMappingView> FieldMappings,
        JsonElement? Expression = null,
        string? Identity = null);
}
