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
            var tester = new HttpApiSourceConnectionTester();
            var result = await tester.TestAsync("{\"authentication\":{\"type\":\"none\"}}", CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(0, factory.RequestCount);
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
            var tester = new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption());
            var result = await tester.TestAsync("{\"url\":\"https://93.184.215.14/events\"}", CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(HttpMethod.Head, factory.Method);
        }

        [Fact]
        public async Task PollingRejectsPrivateEndpointBeforeSendingARequest()
        {
            var factory = new FakeFactory();
            var tester = new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption());
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
                new HttpApiSourceConnectionTester(), new FolderSourceConnectionTester(),
                new HttpPollingSourceConnectionTester(factory, new HttpDestinationSecurityOption()),
                new RabbitMqSourceConnectionTester(), new NatsSourceConnectionTester(),
                new KafkaSourceConnectionTester(), new SqsSourceConnectionTester(secrets),
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

        private sealed class UnusedSecrets : ISecretResolver
        {
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("No secret should be resolved.");
        }

        private sealed class FakeFactory : IHttpClientFactory
        {
            public int RequestCount { get; private set; }
            public HttpMethod? Method { get; private set; }
            public HttpClient CreateClient(string name) => new(new Handler(this));

            private sealed class Handler(FakeFactory owner) : HttpMessageHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    owner.RequestCount++;
                    owner.Method = request.Method;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                }
            }
        }
    }
}
