using System.Net;
using System.Text.Json;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;
using NuntivusServer.Application.Deliveries.Execution;

namespace NuntivusServer.Infastructure.Health
{
    public sealed class HttpPollingSourceConnectionTester : ISourceConnectionTester
    {
        private readonly IHttpClientFactory _http;
        private readonly HttpDestinationSecurityOption _security;
        private readonly ISecretResolver _secrets;

        public HttpPollingSourceConnectionTester(IHttpClientFactory http, HttpDestinationSecurityOption security, ISecretResolver secrets)
        {
            _http = http;
            _security = security;
            _secrets = secrets;
        }

        public SourceType Type => SourceType.HTTP_POLLING;

        public Task<Result<string>> TestAsync(string normalizedConfiguration, CancellationToken cancellationToken)
        {
            return ConnectionProbe.RunAsync(async token =>
            {
                using var document = JsonDocument.Parse(normalizedConfiguration);
                var config = document.RootElement;
                var url = config.GetProperty("url").GetString()!;
                if (!HttpDestinationSecurityPolicy.TryValidateDestinationUri(url, _security, out var uri, out var error))
                    return Result.Failure<string>(Error.Validation(error));
                if (!_security.AllowPrivateNetworkDestinations)
                {
                    var addresses = await Dns.GetHostAddressesAsync(uri.Host, token);
                    if (addresses.Any(HttpDestinationSecurityPolicy.IsPrivateNetworkAddress))
                        return Result.Failure<string>(Error.Validation("HTTP endpoint resolves to a private-network address."));
                }
                var secret = await ConnectionProbeCredentials.SourceSecretAsync(config, _secrets, token);
                if (secret.IsFailure) return Result<string>.Fail(secret.Errors);
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                if (config.TryGetProperty("headers", out var headers))
                    foreach (var header in headers.EnumerateObject())
                        request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
                if (secret.Value is not null)
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret.Value);
                using var response = await _http.CreateClient("connection-test").SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, token);
                return response.IsSuccessStatusCode
                    ? Result.Success("HTTP endpoint accepted HEAD with configured headers and credentials. No events retrieved; the configured polling method and retrieval permissions are not verified.")
                    : Result.Failure<string>(Error.Validation($"HTTP endpoint returned {(int)response.StatusCode} to HEAD. No events retrieved; the configured polling method cannot be verified without fetching data."));
            }, cancellationToken);
        }
    }
}
