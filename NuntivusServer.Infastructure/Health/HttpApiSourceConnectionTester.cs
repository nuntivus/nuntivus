using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;
using NuntivusServer.Application.Deliveries.Execution;
using System.Text.Json;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class HttpApiSourceConnectionTester : ISourceConnectionTester
    {
        private readonly ISecretResolver _secrets;

        public HttpApiSourceConnectionTester(ISecretResolver secrets) => _secrets = secrets;

        public SourceType Type => SourceType.HTTP_API;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                if (document.RootElement.TryGetProperty("authentication", out var auth)
                    && auth.GetProperty("type").GetString() == "token")
                {
                    var secret = await _secrets.ResolveAsync(auth.GetProperty("secretRef").GetString()!, token);
                    if (secret.IsFailure) return Result<string>.Fail(secret.Errors);
                    if (string.IsNullOrWhiteSpace(secret.Value))
                        return Result.Failure<string>(Error.Validation("Configured inbound token is empty."));
                }
                return Result.Success("Inbound HTTP source configuration and configured secret are valid. No event ingested; the public ingestion URL and end-to-end authentication are not verified.");
            }, cancellationToken);
    }
}
