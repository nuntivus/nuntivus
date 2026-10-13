using System.Text.Json;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class KafkaSourceConnectionTester : ISourceConnectionTester
    {
        public SourceType Type => SourceType.KAFKA;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(normalizedConfiguration);
            var endpoint = ConnectionProbe.KafkaEndpoint(document.RootElement.GetProperty("bootstrapServers").GetString()!);
            var port = document.RootElement.TryGetProperty("port", out var portElement) && portElement.ValueKind == JsonValueKind.Number
                ? portElement.GetInt32()
                : 9093;
            return endpoint.IsFailure ? Task.FromResult(Result<string>.Fail(endpoint.Errors))
                : ConnectionProbe.RunAsync(token => ConnectionProbe.TestTlsAsync(endpoint.Value, port, token), cancellationToken);
        }
    }
}
