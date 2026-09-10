using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Contracts.Events;

namespace KairoServer.Application.Matching
{
    public interface IContractMatcherService
    {
        /// <summary>
        /// Loads the authoritative Event, resolves matching enabled Contracts
        /// (with enabled Customers), and idempotently creates one Pending
        /// <c>Delivery</c> per (Event, Contract).
        /// </summary>
        Task<Result<MatchResult>> MatchAsync(
            EventAccepted accepted,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Summary of a matcher run for observability and testing.
    /// </summary>
    public sealed record MatchResult(int MatchedContracts, int InsertedDeliveries);
}
