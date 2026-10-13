using System.Text.Json;
using Amazon.Runtime;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Infastructure.Health
{
    internal static class ConnectionProbeCredentials
    {
        internal static async Task<Result<AWSCredentials?>> SourceAwsAsync(JsonElement config,
            ISecretResolver secrets, CancellationToken cancellationToken)
        {
            if (!config.TryGetProperty("secretRef", out var secret))
                return Result.Success<AWSCredentials?>(null);
            var resolved = await secrets.ResolveAsync(secret.GetString()!, cancellationToken);
            return resolved.IsFailure ? Result<AWSCredentials?>.Fail(resolved.Errors)
                : Result.Success<AWSCredentials?>(new BasicAWSCredentials(
                    config.GetProperty("accessKeyId").GetString()!, resolved.Value));
        }

        internal static async Task<Result<string?>> DestinationAsync(DestinationCredential? credential,
            ISecretResolver secrets, CancellationToken cancellationToken)
        {
            if (credential is null) return Result.Success<string?>(null);
            if (credential.Kind == "inline") return Result.Success<string?>(credential.Value);
            var resolved = await secrets.ResolveAsync(credential.Value, cancellationToken);
            return resolved.IsFailure ? Result<string?>.Fail(resolved.Errors) : Result.Success<string?>(resolved.Value);
        }
    }
}
