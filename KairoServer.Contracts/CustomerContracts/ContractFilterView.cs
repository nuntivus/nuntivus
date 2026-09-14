using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractFilterView(
        IReadOnlyCollection<ContractFilterRuleView> MetadataRules,
        IReadOnlyCollection<ContractFilterRuleView> PayloadRules);
}