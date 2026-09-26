using System.Collections.Generic;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractFilterRuleRequest(
        string Selector,
        ContractFilterOperator Operator,
        string? Value = null,
        IReadOnlyCollection<string>? Values = null);
}