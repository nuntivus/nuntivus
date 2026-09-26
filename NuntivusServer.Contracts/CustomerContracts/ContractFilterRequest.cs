using System.Collections.Generic;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractFilterRequest(
        IReadOnlyCollection<ContractFilterRuleRequest>? MetadataRules = null,
        IReadOnlyCollection<ContractFilterRuleRequest>? PayloadRules = null);
}