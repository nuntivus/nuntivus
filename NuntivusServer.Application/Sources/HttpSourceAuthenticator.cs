using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class HttpSourceAuthenticator : IHttpSourceAuthenticator
    {
        private readonly IManagedSecretRepository _secrets;
        private readonly IDestinationCredentialProtector _protector;

        public HttpSourceAuthenticator(IManagedSecretRepository secrets, IDestinationCredentialProtector protector)
        {
            _secrets = secrets;
            _protector = protector;
        }

        public async Task<Result> AuthenticateAsync(Source source, string? token, CancellationToken cancellationToken)
        {
            if (source.Type != SourceType.HTTP_API)
                return Result.Failure(Error.Validation("Source is not an HTTP Source."));

            var validation = HttpSourceConfigurationValidator.Normalize(source.Configuration);
            if (validation.IsFailure) return Result.Failure(validation.Errors);

            using var document = JsonDocument.Parse(validation.Value);
            var auth = document.RootElement.GetProperty("authentication");
            if (auth.GetProperty("type").GetString() == "none") return Result.Success();

            if (string.IsNullOrEmpty(token)) return Result.Failure(Error.Unauthorized("Invalid Source token."));
            var secretRef = auth.GetProperty("secretRef").GetString()!;
            var encrypted = await _secrets.GetEncryptedValueAsync(secretRef, cancellationToken);
            if (encrypted is null) return Result.Failure(Error.Unauthorized("Invalid Source token."));

            var resolved = _protector.Unprotect(encrypted);
            if (resolved.IsFailure) return Result.Failure(resolved.Errors);

            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(resolved.Value));
            return CryptographicOperations.FixedTimeEquals(actual, expected)
                ? Result.Success()
                : Result.Failure(Error.Unauthorized("Invalid Source token."));
        }
    }
}
