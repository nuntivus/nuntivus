using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractFilterRequest(
        IReadOnlyCollection<ContractFilterRuleRequest>? MetadataRules = null,
        IReadOnlyCollection<ContractFilterRuleRequest>? PayloadRules = null);
}