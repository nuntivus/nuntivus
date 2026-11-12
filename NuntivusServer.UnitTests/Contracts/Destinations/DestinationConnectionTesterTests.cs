using System.Net;
using System.Net.Sockets;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Infastructure.Health;
using NuntivusServer.Infastructure.Deliveries.Http;
using NuntivusServer.Common.Logging;
using System.Text.Json;
using NuntivusServer.Contracts.CustomerContracts;
using DestinationType = NuntivusServer.Domain.Contracts.DestinationType;
using NuntivusServer.Infastructure.Deliveries.Kafka;
using NuntivusServer.Infastructure.Deliveries.Nats;
using NuntivusServer.Infastructure.Deliveries.RabbitMq;
using NuntivusServer.Infastructure.Deliveries.Sqs;

namespace NuntivusServer.UnitTests.Contracts.Destinations
{
    public sealed class DestinationConnectionTesterTests
    {
        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        [InlineData("GET")]
        public async Task HttpTestSendsConfiguredRequest(string method)
        {
            var handler = new HeadHandler();
            var tester = BuildTester(handler);
            var destination = new Destination(DestinationType.Http,
                JsonSerializer.Serialize(new HttpDestinationConfig("https://example.test/events",
                    new HttpAuthentication("bearer", new HttpCredential("ref", "secret-ref")),
                    Headers: new Dictionary<string, string> { ["X-Test"] = "configured" },
                    ProtectedHeaders: new Dictionary<string, string> { ["X-Secret"] = "secret-ref" }, Method: method)));
            var result = await tester.TestAsync(destination, "{\"test\":true}", CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(method, handler.Method?.Method);
            Assert.Equal(method == "GET" ? null : "{\"test\":true}", handler.Body);
            Assert.Equal("Bearer resolved-secret", handler.Authorization);
            Assert.Equal("configured", handler.Header);
            Assert.Equal("resolved-secret", handler.SecretHeader);
            Assert.Equal(1, handler.RequestCount);
        }

        [Fact]
        public async Task HttpNotFoundFailsConfiguredMethod()
        {
            var handler = new HeadHandler(HttpStatusCode.NotFound);
            var tester = BuildTester(handler);
            var destination = new Destination(DestinationType.Http,
                "{\"Url\":\"https://example.test/webhook\",\"Method\":\"PUT\",\"Authentication\":{\"Type\":\"none\"}}");

            var result = await tester.TestAsync(destination, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains("PUT", result.Errors[0].Message);
            Assert.Equal(HttpMethod.Put, handler.Method);
            Assert.Equal(1, handler.RequestCount);
            Assert.True(handler.HasBody);
        }

        [Theory]
        [InlineData(HttpStatusCode.MethodNotAllowed, false)]
        [InlineData(HttpStatusCode.OK, true)]
        [InlineData(HttpStatusCode.Unauthorized, false)]
        [InlineData(HttpStatusCode.Forbidden, false)]
        [InlineData(HttpStatusCode.InternalServerError, false)]
        public async Task HttpTestUsesDeliveryStatusHandling(HttpStatusCode status, bool success)
        {
            var handler = new HeadHandler(status);
            var tester = BuildTester(handler);
            var destination = new Destination(DestinationType.Http,
                "{\"Url\":\"https://example.test/webhook\",\"Authentication\":{\"Type\":\"none\"}}");
            var result = await tester.TestAsync(destination, CancellationToken.None);
            Assert.Equal(success, result.IsSuccess);
            if (success) Assert.Contains("POST", result.Value);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal(1, handler.RequestCount);
        }

        [Fact]
        public void EachDestinationTypeHasItsOwnTester()
        {
            var secrets = new UnusedSecrets();
            IDestinationConnectionTester[] testers =
            [
                BuildTester(new HeadHandler()),
                new KafkaDestinationConnectionTester(new KafkaDeliveryExecutor(secrets, new TestLogger<KafkaDeliveryExecutor>())),
                new NatsDestinationConnectionTester(new NatsDeliveryExecutor(secrets, new TestLogger<NatsDeliveryExecutor>())),
                new RabbitMqDestinationConnectionTester(new RabbitMqDeliveryExecutor(secrets, new TestLogger<RabbitMqDeliveryExecutor>())),
                new SqsDestinationConnectionTester(new SqsDeliveryExecutor(secrets, new TestLogger<SqsDeliveryExecutor>()))
            ];
            Assert.Equal(Enum.GetValues<DestinationType>().Order(), testers.Select(tester => tester.Type).Order());
            Assert.Equal(testers.Length, testers.Select(tester => tester.GetType()).Distinct().Count());
        }

        [Theory]
        [InlineData(SocketError.ConnectionRefused, "Connection refused")]
        [InlineData(SocketError.HostNotFound, "hostname could not be resolved")]
        [InlineData(SocketError.NetworkUnreachable, "unreachable from the API host")]
        [InlineData(SocketError.ConnectionReset, "closed or reset")]
        public async Task HttpTestExplainsSocketFailureWithoutExposingExceptionSecrets(SocketError error, string expected)
        {
            var handler = new HeadHandler { Failure = new HttpRequestException("secret-in-exception", new SocketException((int)error)) };
            var result = await BuildTester(handler).TestAsync(new Destination(DestinationType.Http,
                """{"Url":"https://example.test/webhook?token=private","Authentication":{"Type":"none"}}"""), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Contains(expected, result.Errors[0].Message);
            Assert.DoesNotContain("secret-in-exception", result.Errors[0].Message);
            Assert.DoesNotContain("private", result.Errors[0].Message);
            Assert.Equal(1, handler.RequestCount);
        }

        [Theory]
        [InlineData(HttpRequestError.SecureConnectionError, "TLS negotiation")]
        [InlineData(HttpRequestError.NameResolutionError, "hostname could not be resolved")]
        [InlineData(HttpRequestError.HttpProtocolError, "invalid HTTP protocol")]
        public async Task HttpTestExplainsHttpTransportFailure(HttpRequestError error, string expected)
        {
            var handler = new HeadHandler { Failure = new HttpRequestException(error, "secret-in-exception") };
            var result = await BuildTester(handler).TestAsync(new Destination(DestinationType.Http,
                """{"Url":"https://example.test/webhook","Authentication":{"Type":"none"}}"""), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Contains(expected, result.Errors[0].Message);
            Assert.DoesNotContain("secret-in-exception", result.Errors[0].Message);
        }

        [Fact]
        public async Task HttpTestExplainsTimeoutAndDoesNotRetry()
        {
            var handler = new HeadHandler { Failure = new OperationCanceledException() };
            var result = await BuildTester(handler).TestAsync(new Destination(DestinationType.Http,
                """{"Url":"https://example.test/webhook","Authentication":{"Type":"none"}}"""), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Contains("may already have processed", result.Errors[0].Message);
            Assert.Equal(1, handler.RequestCount);
        }

        private static HttpDestinationConnectionTester BuildTester(HeadHandler handler) => new(new HttpDeliveryExecutor(
            new Factory(handler), new ResolvedSecrets(), new TestLogger<HttpDeliveryExecutor>()));

        private sealed class TestLogger<T> : IAppLogger<T>
        {
            public void Debug(string messageTemplate, params object?[] args) { }
            public void Information(string messageTemplate, params object?[] args) { }
            public void Warning(string messageTemplate, params object?[] args) { }
            public void Error(string messageTemplate, params object?[] args) { }
            public void Error(Exception exception, string messageTemplate, params object?[] args) { }
            public void Fatal(string messageTemplate, params object?[] args) { }
            public void Fatal(Exception exception, string messageTemplate, params object?[] args) { }
        }

        private sealed class ResolvedSecrets : ISecretResolver
        {
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success("resolved-secret"));
        }

        private sealed class Factory(HeadHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, false);
        }

        private sealed class HeadHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
        {
            public Exception? Failure { get; init; }
            public HttpMethod? Method { get; private set; }
            public int RequestCount { get; private set; }
            public bool HasBody { get; private set; }
            public string? Body { get; private set; }
            public string? Authorization { get; private set; }
            public string? Header { get; private set; }
            public string? SecretHeader { get; private set; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Method = request.Method;
                RequestCount++;
                if (Failure is not null) throw Failure;
                HasBody = request.Content is not null;
                Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Authorization = request.Headers.Authorization?.ToString();
                Header = request.Headers.TryGetValues("X-Test", out var headers) ? headers.Single() : null;
                SecretHeader = request.Headers.TryGetValues("X-Secret", out var secrets) ? secrets.Single() : null;
                return new HttpResponseMessage(status);
            }
        }

        private sealed class UnusedSecrets : ISecretResolver
        {
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("No secret should be resolved.");
        }
    }
}
