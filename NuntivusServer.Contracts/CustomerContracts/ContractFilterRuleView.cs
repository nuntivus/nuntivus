using System.Collections.Generic;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractFilterRuleView(
        string Selector,
        ContractFilterOperator Operator,
        string? Value,
        IReadOnlyCollection<string> Values);
}