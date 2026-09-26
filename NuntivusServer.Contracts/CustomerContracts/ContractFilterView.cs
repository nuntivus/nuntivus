using System.Collections.Generic;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractFilterView(
        IReadOnlyCollection<ContractFilterRuleView> MetadataRules,
        IReadOnlyCollection<ContractFilterRuleView> PayloadRules);
}