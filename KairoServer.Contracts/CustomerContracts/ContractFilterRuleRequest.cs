using System.Collections.Generic;

namespace KairoServer.Contracts.CustomerContracts
{
    public sealed record ContractFilterRuleRequest(
        string Selector,
        ContractFilterOperator Operator,
        string? Value = null,
        IReadOnlyCollection<string>? Values = null);
}