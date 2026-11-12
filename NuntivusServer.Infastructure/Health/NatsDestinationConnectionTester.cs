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
        private readonly NuntivusServer.Infastructure.Deliveries.Nats.NatsDeliveryExecutor _executor;

        public NatsDestinationConnectionTester(NuntivusServer.Infastructure.Deliveries.Nats.NatsDeliveryExecutor executor) => _executor = executor;

        public DestinationType Type => DestinationType.Nats;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken) =>
            TestAsync(destination, "{}", cancellationToken);

        public Task<Result<string>> TestAsync(Destination destination, string payload, CancellationToken cancellationToken) =>
            DestinationTestRequest.SendAsync(_executor, destination, payload, cancellationToken);
    }
}
