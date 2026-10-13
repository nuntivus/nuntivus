using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class NatsDestinationConnectionTester : IDestinationConnectionTester
    {
        public DestinationType Type => DestinationType.Nats;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(token =>
            {
                var config = JsonSerializer.Deserialize<NatsDestinationConfig>(destination.Configuration)!;
                var uri = new Uri(config.Url);
                var port = uri.Port == -1 ? 4222 : uri.Port;
                return ConnectionProbe.TestTlsAsync(uri, port, token);
            }, cancellationToken);
    }
}
