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
        public DestinationType Type => DestinationType.Kafka;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken)
        {
            var config = JsonSerializer.Deserialize<KafkaDestinationConfig>(destination.Configuration)!;
            var endpoint = ConnectionProbe.KafkaEndpoint(config.BootstrapServers);
            if (endpoint.IsFailure) 
                return Task.FromResult(Result<string>.Fail(endpoint.Errors));
            var port = config.Credentials?.SecurityProtocol?.Equals("PLAINTEXT", StringComparison.OrdinalIgnoreCase) == true
                || config.Credentials?.SecurityProtocol?.Equals("SASL_PLAINTEXT", StringComparison.OrdinalIgnoreCase) == true
                ? 9092 : 9093;
            var plaintext = config.Credentials?.SecurityProtocol?.Equals("PLAINTEXT", StringComparison.OrdinalIgnoreCase) == true
                || config.Credentials?.SecurityProtocol?.Equals("SASL_PLAINTEXT", StringComparison.OrdinalIgnoreCase) == true;
            return ConnectionProbe.RunAsync(token => plaintext
                ? ConnectionProbe.TestTcpAsync(endpoint.Value, port, token)
                : ConnectionProbe.TestTlsAsync(endpoint.Value, port, token), cancellationToken);
        }
    }
}
