using System.Threading;
using System.Threading.Tasks;
using KairoServer.Domain.Sources;

namespace KairoServer.Application.Sources
{
    /// <summary>
    /// Seam implemented by ingestion adapters (HTTP, SDK, Kafka, Stripe webhook, etc.)
    /// that translate a transport-specific producer notion into a canonical
    /// <see cref="SourceId"/> plus a normalized event payload.
    /// </summary>
    /// <remarks>
    /// This interface intentionally has no implementations yet. It exists to formalize
    /// the boundary between transport-specific ingestion and the canonical domain,
    /// and to prepare the Source model for adapters. Concrete adapter contracts
    /// (payload shape, delivery semantics) will be added when the first adapter
    /// beyond the current HTTP API is implemented.
    /// </remarks>
    public interface ISourceAdapter
    {
        /// <summary>
        /// The category of adapter (used for diagnostics and adapter registration).
        /// </summary>
        SourceType Type { get; }

        /// <summary>
        /// Resolves the canonical <see cref="SourceId"/> for the given adapter-specific context.
        /// Adapters are responsible for enforcing that the returned id is stable and
        /// consistent with <see cref="SourceId"/> identity rules.
        /// </summary>
        Task<SourceId> ResolveSourceIdAsync(object adapterContext, CancellationToken cancellationToken);
    }
}
