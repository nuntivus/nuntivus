using System;

namespace KairoServer.Application.Persistence
{
    /// <summary>
    /// Minimal projection returned by <see cref="IContractRepository.ListEnabledMatchingAsync"/>.
    /// Contains just the identifiers the matcher needs to fan out deliveries,
    /// avoiding the cost of hydrating full <c>Contract</c> aggregates on the hot path.
    /// </summary>
    public sealed record ContractMatch(Guid ContractId, string CustomerId);
}
