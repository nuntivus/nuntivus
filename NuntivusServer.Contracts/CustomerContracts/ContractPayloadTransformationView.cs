using System.Collections.Generic;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractPayloadTransformationView(
        int Version,
        IReadOnlyCollection<ContractPayloadFieldMappingView> FieldMappings);
}
