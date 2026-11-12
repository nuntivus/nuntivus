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
        private readonly NuntivusServer.Infastructure.Deliveries.RabbitMq.RabbitMqDeliveryExecutor _executor;

        public RabbitMqDestinationConnectionTester(NuntivusServer.Infastructure.Deliveries.RabbitMq.RabbitMqDeliveryExecutor executor) => _executor = executor;

        public DestinationType Type => DestinationType.RabbitMq;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken) =>
            TestAsync(destination, "{}", cancellationToken);

        public Task<Result<string>> TestAsync(Destination destination, string payload, CancellationToken cancellationToken) =>
            DestinationTestRequest.SendAsync(_executor, destination, payload, cancellationToken);
    }
}
