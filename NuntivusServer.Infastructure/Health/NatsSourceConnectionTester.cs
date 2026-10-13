using System.Text.Json;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class NatsSourceConnectionTester : ISourceConnectionTester
    {
        public SourceType Type => SourceType.NATS;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(normalizedConfiguration);
            var uri = new Uri(document.RootElement.GetProperty("url").GetString()!);
            var port = uri.Port == -1 ? 4222 : uri.Port;
            return ConnectionProbe.RunAsync(token => ConnectionProbe.TestTlsAsync(uri, port, token), cancellationToken);
        }
    }
}
