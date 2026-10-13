using System.Text.Json;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class RabbitMqSourceConnectionTester : ISourceConnectionTester
    {
        public SourceType Type => SourceType.RABBITMQ;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(normalizedConfiguration);
            var uri = new Uri(document.RootElement.GetProperty("uri").GetString()!);
            var port = document.RootElement.GetProperty("port").GetInt32();
            return ConnectionProbe.RunAsync(token => ConnectionProbe.TestTlsAsync(uri, port, token), cancellationToken);
        }
    }
}
