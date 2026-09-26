using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Deliveries.Http
{
    /// <summary>
    /// Placeholder <see cref="ISecretResolver"/> that fails every lookup.
    /// Keeps the HTTP <c>none</c> auth path working end-to-end while the real
    /// secret store is being designed; <c>hmac</c>/<c>bearer</c> destinations
    /// will surface a <see cref="DeliveryFailureCategory.ConfigurationInvalid"/>
    /// failure until a real resolver is wired in.
    /// </summary>
    public sealed class NullSecretResolver : ISecretResolver
    {
        public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Failure<string>(Error.Validation(
                "Secret resolution is not configured. Register a real ISecretResolver.")));
        }
    }
}
