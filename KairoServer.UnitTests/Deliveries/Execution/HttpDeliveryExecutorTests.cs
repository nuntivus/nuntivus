using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Common;
using KairoServer.Common.Logging;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;
using KairoServer.Infastructure.Deliveries.Http;
using DomainDestinationType = KairoServer.Domain.Contracts.DestinationType;

namespace KairoServer.UnitTests.Deliveries.Execution
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

        // --- helpers ---------------------------------------------------------

        private static HttpDeliveryExecutor BuildExecutor(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            var factory = new StubHttpClientFactory(handler);
            return new HttpDeliveryExecutor(
                factory,
                new StubSecretResolver(),
                new NullAppLogger<HttpDeliveryExecutor>());
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
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success("secret-value"));
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
