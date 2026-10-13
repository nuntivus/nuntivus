using System.Net;
using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Infastructure.Health;

namespace NuntivusServer.UnitTests.Contracts.Destinations
{
    public sealed class DestinationConnectionTesterTests
    {
        [Fact]
        public async Task HttpTestSendsHeadNotPost()
        {
            var handler = new HeadHandler();
            var tester = new HttpDestinationConnectionTester(new Factory(handler));
            var destination = new Destination(DestinationType.Http,
                "{\"Url\":\"https://example.com/events\",\"Authentication\":{\"Type\":\"none\"}}");
            var result = await tester.TestAsync(destination, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(HttpMethod.Head, handler.Method);
        }

        [Fact]
        public void EachDestinationTypeHasItsOwnTester()
        {
            var secrets = new UnusedSecrets();
            IDestinationConnectionTester[] testers =
            [
                new HttpDestinationConnectionTester(new Factory(new HeadHandler())),
                new KafkaDestinationConnectionTester(), new NatsDestinationConnectionTester(),
                new RabbitMqDestinationConnectionTester(), new SqsDestinationConnectionTester(secrets)
            ];
            Assert.Equal(Enum.GetValues<DestinationType>().Order(), testers.Select(tester => tester.Type).Order());
            Assert.Equal(testers.Length, testers.Select(tester => tester.GetType()).Distinct().Count());
        }

        private sealed class Factory(HeadHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, false);
        }

        private sealed class HeadHandler : HttpMessageHandler
        {
            public HttpMethod? Method { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Method = request.Method;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }

        private sealed class UnusedSecrets : ISecretResolver
        {
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("No secret should be resolved.");
        }
    }
}
