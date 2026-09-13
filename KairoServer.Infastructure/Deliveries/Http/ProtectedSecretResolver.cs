using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Common;

namespace KairoServer.Infastructure.Deliveries.Http
{
    public sealed class ProtectedSecretResolver : ISecretResolver
    {
        private readonly IDestinationCredentialProtector _protector;

        public ProtectedSecretResolver(IDestinationCredentialProtector protector)
        {
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken)
        {
            if (!ConfigReference.TryParse(secretRef, out var reference))
            {
                return Task.FromResult(Result.Failure<string>(Error.Validation(
                    "Secret reference format is invalid.")));
            }

            if (reference.Scheme.Equals("protected", StringComparison.Ordinal))
            {
                var payload = reference.Path.TrimStart('.');
                return Task.FromResult(_protector.Unprotect(payload));
            }

            if (reference.Scheme.Equals("secret", StringComparison.Ordinal))
            {
                return Task.FromResult(Result.Failure<string>(Error.Validation(
                    "External secret resolution is not configured. Register a resolver for 'secret' references.")));
            }

            return Task.FromResult(Result.Failure<string>(Error.Validation(
                $"Unsupported secret reference scheme '{reference.Scheme}'.")));
        }
    }
}
