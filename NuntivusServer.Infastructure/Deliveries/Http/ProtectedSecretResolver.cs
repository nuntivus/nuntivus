using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Deliveries.Http
{
    public sealed class ProtectedSecretResolver : ISecretResolver
    {
        private readonly IDestinationCredentialProtector _protector;
        private readonly IManagedSecretRepository _secrets;

        public ProtectedSecretResolver(IDestinationCredentialProtector protector, IManagedSecretRepository secrets)
        {
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        }

        public async Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken)
        {
            if (!ConfigReference.TryParse(secretRef, out var reference))
            {
                return Result.Failure<string>(Error.Validation("Secret reference format is invalid."));
            }

            if (reference.Scheme.Equals("protected", StringComparison.Ordinal))
            {
                var payload = reference.Path.TrimStart('.');
                return _protector.Unprotect(payload);
            }

            if (reference.Scheme.Equals("secret", StringComparison.Ordinal))
            {
                var encrypted = await _secrets.GetEncryptedValueAsync(reference.ToString(), cancellationToken);
                return encrypted is null
                    ? Result.Failure<string>(Error.Validation("Secret reference was not found."))
                    : _protector.Unprotect(encrypted);
            }

            return Result.Failure<string>(Error.Validation(
                $"Unsupported secret reference scheme '{reference.Scheme}'."));
        }
    }
}
