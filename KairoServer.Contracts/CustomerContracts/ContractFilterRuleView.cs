using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractFilterRuleView(
        string Selector,
        ContractFilterOperator Operator,
        string? Value,
        IReadOnlyCollection<string> Values);
}