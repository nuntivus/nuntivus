using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class KafkaDestinationConnectionTester : IDestinationConnectionTester
    {
        private readonly NuntivusServer.Infastructure.Deliveries.Kafka.KafkaDeliveryExecutor _executor;

        public KafkaDestinationConnectionTester(NuntivusServer.Infastructure.Deliveries.Kafka.KafkaDeliveryExecutor executor) => _executor = executor;

        public DestinationType Type => DestinationType.Kafka;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken)
            => TestAsync(destination, "{}", cancellationToken);

        public Task<Result<string>> TestAsync(Destination destination, string payload, CancellationToken cancellationToken) =>
            DestinationTestRequest.SendAsync(_executor, destination, payload, cancellationToken);
    }
}
