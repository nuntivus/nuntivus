using System.Net;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;
using NuntivusServer.Infastructure.Health;

namespace NuntivusServer.UnitTests.Sources
{
    public sealed class ConnectionTesterTests
    {
        [Fact]
        public async Task InboundHttpDoesNotSendARequest()
        {
            var factory = new FakeFactory();
            var tester = new HttpApiSourceConnectionTester(new UnusedSecrets());
            var result = await tester.TestAsync("{\"authentication\":{\"type\":\"none\"}}", CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(0, factory.RequestCount);
        }

        [Fact]
        public async Task PollingUsesConfiguredCredentialsAndHeadersWithoutFetchingData()
        {
            var factory = new FakeFactory();
            var tester = new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption(), new ResolvedSecrets());
            var result = await tester.TestAsync("""{"url":"https://93.184.215.14/events","method":"POST","body":{"page":1},"headers":{"X-Client":"configured"},"secretRef":"secret://test"}""", CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(HttpMethod.Head, factory.Method);
            Assert.Equal("Bearer test-secret", factory.Authorization);
            Assert.Equal("configured", factory.ClientHeader);
            Assert.False(factory.HasBody);
            Assert.Equal(1, factory.RequestCount);
        }

        [Fact]
        public async Task PollingDoesNotFallBackToFetchingWhenHeadIsRejected()
        {
            var factory = new FakeFactory { Status = HttpStatusCode.MethodNotAllowed };
            var tester = new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption(), new UnusedSecrets());
            var result = await tester.TestAsync("""{"url":"https://93.184.215.14/events"}""", CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal(HttpMethod.Head, factory.Method);
            Assert.Equal(1, factory.RequestCount);
        }

        [Fact]
        public async Task InboundHttpResolvesConfiguredTokenWithoutIngesting()
        {
            var secrets = new ResolvedSecrets();
            var result = await new HttpApiSourceConnectionTester(secrets).TestAsync(
                """{"authentication":{"type":"token","secretRef":"secret://test"}}""", CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(1, secrets.Calls);
            Assert.DoesNotContain("test-secret", result.Value);
        }

        [Fact]
        public async Task FolderTestDoesNotModifySourceFiles()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var file = Path.Combine(path, "event.json");
            try
            {
                await File.WriteAllTextAsync(file, "{}");
                var result = await new FolderSourceConnectionTester().TestAsync(
                    System.Text.Json.JsonSerializer.Serialize(new { path }), CancellationToken.None);
                Assert.True(result.IsSuccess);
                Assert.Equal("{}", await File.ReadAllTextAsync(file));
                Assert.Single(Directory.GetFiles(path));
            }
            finally { Directory.Delete(path, true); }
        }

        private sealed class ResolvedSecrets : ISecretResolver
        {
            public int Calls { get; private set; }
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Result.Success("test-secret"));
            }
        }

        [Fact]
        public async Task MissingFolderReturnsFailure()
        {
            var tester = new FolderSourceConnectionTester();
            var result = await tester.TestAsync("{\"path\":\"/not-a-real-nuntivus-source-folder\"}", CancellationToken.None);
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task PollingUsesHeadWithoutConsumingAnEvent()
        {
            var factory = new FakeFactory();
            var tester = new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption(), new UnusedSecrets());
            var result = await tester.TestAsync("{\"url\":\"https://93.184.215.14/events\"}", CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(HttpMethod.Head, factory.Method);
        }

        [Fact]
        public async Task PollingRejectsPrivateEndpointBeforeSendingARequest()
        {
            var factory = new FakeFactory();
            var tester = new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption(), new UnusedSecrets());
            var result = await tester.TestAsync("{\"url\":\"https://127.0.0.1/events\"}", CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal(0, factory.RequestCount);
        }

        [Fact]
        public void EachOperationalSourceTypeHasItsOwnTester()
        {
            var factory = new FakeFactory();
            var secrets = new UnusedSecrets();
            ISourceConnectionTester[] testers =
            [
                new HttpApiSourceConnectionTester(secrets), new FolderSourceConnectionTester(),
                new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption(), secrets),
                new RabbitMqSourceConnectionTester(secrets), new NatsSourceConnectionTester(secrets),
                new KafkaSourceConnectionTester(secrets), new SqsSourceConnectionTester(secrets),
                new S3SourceConnectionTester(secrets)
            ];
            SourceType[] operational =
            [
                SourceType.HTTP_API, SourceType.FOLDER, SourceType.HTTP_POLLING, SourceType.RABBITMQ,
                SourceType.NATS, SourceType.KAFKA, SourceType.SQS, SourceType.S3
            ];
            Assert.Equal(operational.Order(), testers.Select(tester => tester.Type).Order());
            Assert.Equal(8, testers.Select(tester => tester.GetType()).Distinct().Count());
        }

        [Theory]
        [InlineData(SourceType.HTTP_API, "{\"authentication\":{\"type\":\"token\",\"secretRef\":\"secret.probe.missing\"}}")]
        [InlineData(SourceType.HTTP_POLLING, "{\"url\":\"https://example.com/events\",\"secretRef\":\"secret.probe.missing\"}")]
        [InlineData(SourceType.RABBITMQ, "{\"uri\":\"amqps://broker.example.com\",\"queue\":\"events\",\"secretRef\":\"secret.probe.missing\"}")]
        [InlineData(SourceType.KAFKA, "{\"bootstrapServers\":\"broker:9093\",\"topic\":\"events\",\"username\":\"probe\",\"secretRef\":\"secret.probe.missing\"}")]
        [InlineData(SourceType.NATS, "{\"url\":\"tls://broker.example.com\",\"stream\":\"EVENTS\",\"subject\":\"events\",\"durable\":\"probe\",\"secretRef\":\"secret.probe.missing\"}")]
        [InlineData(SourceType.SQS, "{\"region\":\"us-east-1\",\"queueUrl\":\"https://sqs.us-east-1.amazonaws.com/123/events\",\"accessKeyId\":\"fixture-id\",\"secretRef\":\"secret.probe.missing\"}")]
        [InlineData(SourceType.S3, "{\"region\":\"us-east-1\",\"bucket\":\"events\",\"prefix\":\"incoming/\",\"accessKeyId\":\"fixture-id\",\"secretRef\":\"secret.probe.missing\"}")]
        public async Task Missing_managed_credentials_stop_each_probe_before_network(SourceType type, string config)
        {
            var secrets = new MissingSecrets();
            var factory = new FakeFactory();
            ISourceConnectionTester tester = type switch
            {
                SourceType.HTTP_API => new HttpApiSourceConnectionTester(secrets),
                SourceType.HTTP_POLLING => new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption(), secrets),
                SourceType.RABBITMQ => new RabbitMqSourceConnectionTester(secrets),
                SourceType.KAFKA => new KafkaSourceConnectionTester(secrets),
                SourceType.NATS => new NatsSourceConnectionTester(secrets),
                SourceType.SQS => new SqsSourceConnectionTester(secrets),
                SourceType.S3 => new S3SourceConnectionTester(secrets),
                _ => throw new InvalidOperationException(type.ToString())
            };
            var result = await tester.TestAsync(config, CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal(1, secrets.Calls);
            Assert.Equal(0, factory.RequestCount);
        }

        private sealed class MissingSecrets : ISecretResolver
        {
            public int Calls { get; private set; }
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Result.Failure<string>(NuntivusServer.Common.Error.Validation("Managed credential unavailable.")));
            }
        }

        private sealed class UnusedSecrets : ISecretResolver
        {
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("No secret should be resolved.");
        }

        private sealed class FakeFactory : IHttpClientFactory
        {
            public int RequestCount { get; private set; }
            public HttpMethod? Method { get; private set; }
            public string? Authorization { get; private set; }
            public string? ClientHeader { get; private set; }
            public bool HasBody { get; private set; }
            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
            public HttpClient CreateClient(string name) => new(new Handler(this));

            private sealed class Handler(FakeFactory owner) : HttpMessageHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    owner.RequestCount++;
                    owner.Method = request.Method;
                    owner.Authorization = request.Headers.Authorization?.ToString();
                    owner.ClientHeader = request.Headers.TryGetValues("X-Client", out var values) ? values.Single() : null;
                    owner.HasBody = request.Content is not null;
                    return Task.FromResult(new HttpResponseMessage(owner.Status));
                }
            }
        }
    }
}
