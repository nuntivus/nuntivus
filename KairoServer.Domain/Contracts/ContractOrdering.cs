using System;

namespace KairoServer.Domain.Contracts
{
    public sealed record ContractOrdering(
        string? KeySelector = null)
    {
        public static ContractOrdering Create(string? keySelector = null) =>
            new(string.IsNullOrWhiteSpace(keySelector) ? null : keySelector.Trim());
    }
}
