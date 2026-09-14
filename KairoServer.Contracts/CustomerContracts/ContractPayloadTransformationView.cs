using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadTransformationView(
        int Version,
        IReadOnlyCollection<ContractPayloadFieldMappingView> FieldMappings);
}
