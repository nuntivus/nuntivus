using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Resolves a secret referenced by a destination configuration
    /// (e.g. <c>secret.customers/acme/webhook-hmac</c>) into its raw value.
    /// Destination-specific and out of scope for Delivery Core; implementations
    /// live in the infrastructure layer.
    /// </summary>
    public interface ISecretResolver
    {
        Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken);
    }
}
