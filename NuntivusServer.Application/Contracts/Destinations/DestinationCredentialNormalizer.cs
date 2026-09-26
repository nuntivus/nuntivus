using System;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Application.Contracts.Destinations
{
    internal sealed class DestinationCredentialNormalizer
    {
        public const int MaxCredentialValueLength = 4096;
        public const string SecretRefScheme = "secret";
        public const string ProtectedRefScheme = "protected";

        private readonly IDestinationCredentialProtector _protector;

        public DestinationCredentialNormalizer(IDestinationCredentialProtector protector)
        {
            _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        public Result<DestinationCredential?> Normalize(DestinationCredential? credential, string fieldName)
        {
            if (credential is null)
                return Result.Success<DestinationCredential?>(null);

            if (string.IsNullOrWhiteSpace(credential.Kind))
                return Result.Failure<DestinationCredential?>(Error.Validation($"{fieldName}.kind is required."));

            var kind = credential.Kind.Trim().ToLowerInvariant();
            if (kind is not "ref" and not "inline")
                return Result.Failure<DestinationCredential?>(Error.Validation($"{fieldName}.kind must be 'ref' or 'inline'."));

            if (string.IsNullOrWhiteSpace(credential.Value))
                return Result.Failure<DestinationCredential?>(Error.Validation($"{fieldName}.value is required."));

            var value = credential.Value.Trim();
            if (value.Length > MaxCredentialValueLength)
                return Result.Failure<DestinationCredential?>(Error.Validation($"{fieldName}.value exceeds {MaxCredentialValueLength} characters."));

            var version = credential.Version is > 0 ? credential.Version : null;

            if (kind == "ref")
            {
                if (!IsReferenceValue(value))
                {
                    return Result.Failure<DestinationCredential?>(Error.Validation(
                        $"{fieldName}.value must start with '{SecretRefScheme}.' or '{ProtectedRefScheme}.' when kind is 'ref'."));
                }

                return Result.Success<DestinationCredential?>(new DestinationCredential("ref", value, version));
            }

            var protectedResult = _protector.Protect(value);
            if (protectedResult.IsFailure)
                return Result<DestinationCredential?>.Fail(protectedResult.Errors);

            return Result.Success<DestinationCredential?>(new DestinationCredential(
                "ref",
                $"{ProtectedRefScheme}.{protectedResult.Value}",
                version));
        }

        public static DestinationCredential? Redact(DestinationCredential? credential) =>
            credential is null
                ? null
                : credential with { Value = "[redacted]" };

        private static bool IsReferenceValue(string value) =>
            value.StartsWith($"{SecretRefScheme}.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith($"{ProtectedRefScheme}.", StringComparison.OrdinalIgnoreCase);
    }
}
