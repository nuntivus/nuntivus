using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Infastructure.Deliveries.Http;
using DomainDestinationType = NuntivusServer.Domain.Contracts.DestinationType;

namespace NuntivusServer.UnitTests.Deliveries.Execution
{
    public class HttpDeliveryExecutorTests
    {
        [Theory]
        [InlineData(HttpStatusCode.OK, true, null)]
        [InlineData(HttpStatusCode.NoContent, true, null)]
        [InlineData(HttpStatusCode.BadRequest, false, DeliveryFailureCategory.Permanent)]
        [InlineData(HttpStatusCode.Unauthorized, false, DeliveryFailureCategory.Permanent)]
        [InlineData(HttpStatusCode.NotFound, false, DeliveryFailureCategory.Permanent)]
        [InlineData(HttpStatusCode.RequestTimeout, false, DeliveryFailureCategory.Timeout)]
        [InlineData(HttpStatusCode.TooManyRequests, false, DeliveryFailureCategory.Throttled)]
        [InlineData(HttpStatusCode.InternalServerError, false, DeliveryFailureCategory.Transient)]
        [InlineData(HttpStatusCode.BadGateway, false, DeliveryFailureCategory.Transient)]
        [InlineData(HttpStatusCode.ServiceUnavailable, false, DeliveryFailureCategory.Transient)]
        public async Task Maps_status_code_to_category(
            HttpStatusCode status, bool expectedSuccess, DeliveryFailureCategory? expectedCategory)
        {
            var executor = BuildExecutor(_ => new HttpResponseMessage(status));

            var result = await executor.ExecuteAsync(BuildRequest(), CancellationToken.None);

            Assert.Equal(expectedSuccess, result.Succeeded);
            Assert.Equal(expectedCategory, result.Category);
        }

        [Theory]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public async Task Sends_configured_http_method_with_payload(string method)
        {
            string? sentMethod = null;
            string? sentBody = null;
            var executor = BuildExecutor(request =>
            {
                sentMethod = request.Method.Method;
                sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var result = await executor.ExecuteAsync(BuildRequest(new HttpDestinationConfig(
                "https://example.test/webhook", new HttpAuthentication("none"), Method: method)), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(method, sentMethod);
            Assert.Equal("{\"orderId\":\"o-1\"}", sentBody);
        }

        [Fact]
        public async Task HttpRequestException_maps_to_transient()
        {
            var executor = BuildExecutor(_ => throw new HttpRequestException("boom"));

            var result = await executor.ExecuteAsync(BuildRequest(), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(DeliveryFailureCategory.Transient, result.Category);
        }

        [Fact]
        public async Task Invalid_json_configuration_maps_to_configuration_invalid()
        {
            var executor = BuildExecutor(_ => new HttpResponseMessage(HttpStatusCode.OK));

            var request = BuildRequest(configJson: "not-json");
            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(DeliveryFailureCategory.ConfigurationInvalid, result.Category);
        }

        [Fact]
        public async Task Throttled_response_extracts_retry_after_delta()
        {
            var executor = BuildExecutor(_ =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return resp;
            });

            var result = await executor.ExecuteAsync(BuildRequest(), CancellationToken.None);

            Assert.Equal(DeliveryFailureCategory.Throttled, result.Category);
            Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
        }

        [Fact]
        public async Task Bearer_auth_resolves_secret_and_sets_authorization_header()
        {
            HttpRequestMessage? sent = null;
            var executor = BuildExecutor(req =>
            {
                sent = req;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var result = await executor.ExecuteAsync(BuildRequest(new HttpDestinationConfig(
                Url: "https://example.test/webhook",
                Authentication: new HttpAuthentication("bearer", new HttpCredential("ref", "protected.v1.abcdef")))), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.NotNull(sent);
            Assert.Equal("Bearer", sent!.Headers.Authorization?.Scheme);
            Assert.Equal("secret-value", sent.Headers.Authorization?.Parameter);
        }

        [Fact]
        public async Task Protected_headers_are_resolved_and_injected()
        {
            HttpRequestMessage? sent = null;
            var executor = BuildExecutor(req =>
            {
                sent = req;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var result = await executor.ExecuteAsync(BuildRequest(new HttpDestinationConfig(
                Url: "https://example.test/webhook",
                Authentication: new HttpAuthentication("none"),
                ProtectedHeaders: new Dictionary<string, string>
                {
                    ["X-Api-Key"] = "protected.v1.abcdef"
                })), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.NotNull(sent);
            Assert.True(sent!.Headers.TryGetValues("X-Api-Key", out var values));
            Assert.Equal("secret-value", Assert.Single(values));
        }

        [Fact]
        public async Task Hmac_auth_adds_active_and_overlap_signatures()
        {
            HttpRequestMessage? sent = null;
            var executor = BuildExecutor(req =>
            {
                sent = req;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var result = await executor.ExecuteAsync(BuildRequest(new HttpDestinationConfig(
                Url: "https://example.test/webhook",
                Authentication: new HttpAuthentication("hmac", new HttpCredential(
                    "ref",
                    "protected.v1.new",
                    Version: 2,
                    Previous: new[]
                    {
                        new HttpCredentialSnapshot("ref", "protected.v1.old", 1)
                    })))), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.NotNull(sent);

            Assert.True(sent!.Headers.TryGetValues("X-Nuntivus-Signature", out var activeValues));
            Assert.StartsWith("v2=", Assert.Single(activeValues));

            Assert.True(sent.Headers.TryGetValues("X-Nuntivus-Signature-Previous", out var previousValues));
            Assert.Contains("v1=", Assert.Single(previousValues));

            Assert.True(sent.Headers.TryGetValues("X-Nuntivus-Signature-Timestamp", out var timestampValues));
            Assert.True(long.TryParse(Assert.Single(timestampValues), out _));
        }

        [Fact]
        public async Task Private_network_destination_is_blocked_by_default_policy()
        {
            var executor = BuildExecutor(_ => new HttpResponseMessage(HttpStatusCode.OK));

            var result = await executor.ExecuteAsync(BuildRequest(new HttpDestinationConfig(
                Url: "http://127.0.0.1:9999/webhook",
                Authentication: new HttpAuthentication("none"))), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(DeliveryFailureCategory.ConfigurationInvalid, result.Category);
        }

        [Fact]
        public async Task Response_body_is_bounded_and_marked_truncated()
        {
            var executor = BuildExecutor(
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(new string('x', 50))
                },
                new HttpDestinationSecurityOption
                {
                    AllowPrivateNetworkDestinations = true,
                    MaxResponseBodyBytes = 10,
                });

            var result = await executor.ExecuteAsync(BuildRequest(), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Metadata);
            Assert.True(result.Metadata!.TryGetValue("responseBody", out var responseBody));
            Assert.Equal("xxxxxxxxxx...[truncated]", responseBody);
        }

        // --- helpers ---------------------------------------------------------

        private static HttpDeliveryExecutor BuildExecutor(
            Func<HttpRequestMessage, HttpResponseMessage> handler,
            HttpDestinationSecurityOption? security = null)
        {
            var factory = new StubHttpClientFactory(handler);
            return new HttpDeliveryExecutor(
                factory,
                new StubSecretResolver(),
                new NullAppLogger<HttpDeliveryExecutor>(),
                security);
        }

        private static DeliveryExecutionRequest BuildRequest(string? configJson = null)
        {
            var config = configJson ?? JsonSerializer.Serialize(new HttpDestinationConfig(
                Url: "https://example.test/webhook",
                Authentication: new HttpAuthentication("none")));

            return new DeliveryExecutionRequest(
                DeliveryId: Guid.NewGuid(),
                EventId: Guid.NewGuid(),
                ContractId: Guid.NewGuid(),
                CustomerId: "acme",
                EventType: "orders.created",
                Payload: "{\"orderId\":\"o-1\"}",
                AttemptNumber: 1,
                Destination: new Destination(DomainDestinationType.Http, config));
        }

        private static DeliveryExecutionRequest BuildRequest(HttpDestinationConfig config) =>
            BuildRequest(JsonSerializer.Serialize(config));

        private sealed class StubHttpClientFactory : IHttpClientFactory
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
            public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(new StubHandler(_handler));
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_handler(request));
        }

        private sealed class StubSecretResolver : ISecretResolver
        {
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken)
            {
                if (secretRef.EndsWith(".old", StringComparison.Ordinal))
                    return Task.FromResult(Result.Success("old-secret"));

                if (secretRef.EndsWith(".new", StringComparison.Ordinal))
                    return Task.FromResult(Result.Success("new-secret"));

                return Task.FromResult(Result.Success("secret-value"));
            }
        }

        private sealed class NullAppLogger<T> : IAppLogger<T>
        {
            public void Debug(string messageTemplate, params object?[] args) { }
            public void Information(string messageTemplate, params object?[] args) { }
            public void Warning(string messageTemplate, params object?[] args) { }
            public void Error(string messageTemplate, params object?[] args) { }
            public void Error(Exception exception, string messageTemplate, params object?[] args) { }
            public void Fatal(string messageTemplate, params object?[] args) { }
            public void Fatal(Exception exception, string messageTemplate, params object?[] args) { }
        }
    }
}
