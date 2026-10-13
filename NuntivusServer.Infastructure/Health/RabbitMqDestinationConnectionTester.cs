using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class RabbitMqDestinationConnectionTester : IDestinationConnectionTester
    {
        public DestinationType Type => DestinationType.RabbitMq;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(token =>
            {
                var config = JsonSerializer.Deserialize<RabbitMqDestinationConfig>(destination.Configuration)!;
                var uri = new Uri(config.Uri);
                var port = uri.Port == -1 ? 5671 : uri.Port;
                return ConnectionProbe.TestTlsAsync(uri, port, token);
            }, cancellationToken);
    }
}
