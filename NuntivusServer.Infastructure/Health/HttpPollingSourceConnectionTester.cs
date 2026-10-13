using System.Net;
using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class HttpPollingSourceConnectionTester : ISourceConnectionTester
    {
        private readonly IHttpClientFactory _http;
        private readonly HttpDestinationSecurityOption _security;

        public HttpPollingSourceConnectionTester(IHttpClientFactory http, HttpDestinationSecurityOption security)
        {
            _http = http;
            _security = security;
        }

        public SourceType Type => SourceType.HTTP_POLLING;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(normalizedConfiguration);
            var url = document.RootElement.GetProperty("url").GetString()!;
            return ConnectionProbe.RunAsync(async token =>
            {
                if (!HttpDestinationSecurityPolicy.TryValidateDestinationUri(url, _security, out var uri, out var error))
                    return Result.Failure<string>(Error.Validation(error));
                if (!_security.AllowPrivateNetworkDestinations)
                {
                    var addresses = await Dns.GetHostAddressesAsync(uri.Host, token);
                    if (addresses.Any(HttpDestinationSecurityPolicy.IsPrivateNetworkAddress))
                        return Result.Failure<string>(Error.Validation("HTTP endpoint resolves to a private-network address."));
                }
                return await ConnectionProbe.TestHttpAsync(_http, url,
                    "HTTP endpoint is reachable but does not allow HEAD; event retrieval and credentials are not verified.",
                    "HTTP endpoint responded successfully to HEAD (credentials and event retrieval are not verified).",
                    ".", token);
            }, cancellationToken);
        }
    }
}
