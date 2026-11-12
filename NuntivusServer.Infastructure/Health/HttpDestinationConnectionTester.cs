using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Infastructure.Deliveries.Http;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class HttpDestinationConnectionTester : IDestinationConnectionTester
    {
        private readonly HttpDeliveryExecutor _executor;

        public HttpDestinationConnectionTester(HttpDeliveryExecutor executor) => _executor = executor;

        public DestinationType Type => DestinationType.Http;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken) =>
            TestAsync(destination, "{}", cancellationToken);

        public async Task<Result<string>> TestAsync(Destination destination, string payload, CancellationToken cancellationToken)
        {
            var result = await _executor.ExecuteAsync(new DeliveryExecutionRequest(
                Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "connection-test", "connection-test",
                payload, 1, destination), cancellationToken);
            var config = JsonSerializer.Deserialize<HttpDestinationConfig>(destination.Configuration);
            var method = config?.Method?.ToUpperInvariant() ?? "POST";
            var status = result.Metadata?.GetValueOrDefault("httpStatus");
            var failure = result.Metadata?.GetValueOrDefault("transportFailure");
            var detail = failure switch
            {
                "refused" => "Connection refused. No service is accepting connections on the configured host and port. Check that the receiver is running and use the published host port, not the container's internal port.",
                "dns" => "The destination hostname could not be resolved. Check the hostname and the API host's DNS configuration.",
                "tls" => "TLS negotiation or certificate validation failed. Check HTTP versus HTTPS, the certificate hostname and trust chain. Do not disable certificate validation.",
                "unreachable" => "The destination network or host is unreachable from the API host. Check the address, routing and firewall.",
                "timeout" => "The connection timed out. Check the receiver, firewall and configured timeout before retrying.",
                "reset" => "The receiver closed or reset the connection. Check HTTP versus HTTPS and the receiver logs before retrying.",
                "protocol" => "The receiver returned an invalid HTTP protocol response. Check HTTP versus HTTPS and the receiver configuration.",
                _ when result.Category == DeliveryFailureCategory.Timeout => "The request timed out. Check the configured timeout and receiver logs before retrying; the receiver may already have processed the request.",
                _ => "No HTTP response was received. Check the receiver, published port, HTTP versus HTTPS and connectivity from the API host."
            };
            return result.Succeeded
                ? Result.Success($"HTTP endpoint accepted the {method} test request (status {status}).")
                : Result.Failure<string>(Error.Validation(status is not null
                    ? $"HTTP endpoint returned {status} to the {method} test request."
                    : $"HTTP {method} test failed: {result.Category}. {detail}"));
        }
    }
}
