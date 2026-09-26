using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Application.Security;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    /// <summary>
    /// Seam implemented by ingestion adapters (HTTP, SDK, Kafka, Stripe webhook, etc.)
    /// that translate a transport-specific producer notion into a canonical
    /// <see cref="SourceId"/> plus a normalized event payload.
    /// </summary>
    public interface ISourceAdapter
    {
        /// <summary>
        /// The category of adapter (used for diagnostics and adapter registration).
        /// </summary>
        SourceType Type { get; }

        /// <summary>
        /// Shared endpoint security context used by policy evaluation.
        /// </summary>
        EndpointSecurityContext SecurityContext { get; }

        /// <summary>
        /// Normalizes an adapter-specific context into the canonical source event
        /// representation consumed by the shared ingestion pipeline.
        /// </summary>
        Task<Result<CanonicalSourceEvent>> NormalizeAsync(object adapterContext, CancellationToken cancellationToken);
    }
}
