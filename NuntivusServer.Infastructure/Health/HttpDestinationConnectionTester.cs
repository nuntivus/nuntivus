using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class HttpDestinationConnectionTester : IDestinationConnectionTester
    {
        private readonly IHttpClientFactory _http;

        public HttpDestinationConnectionTester(IHttpClientFactory http) => _http = http;

        public DestinationType Type => DestinationType.Http;

        public Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken) =>
            ConnectionProbe.RunAsync(token =>
            {
                var config = JsonSerializer.Deserialize<HttpDestinationConfig>(destination.Configuration)!;
                return ConnectionProbe.TestHttpAsync(_http, config.Url,
                    "HTTP endpoint is reachable but does not allow HEAD; POST and authentication are not verified.",
                    "HTTP endpoint responded to HEAD. Delivery authentication and POST are not verified.",
                    " (POST may still work).", token);
            }, cancellationToken);
    }
}
