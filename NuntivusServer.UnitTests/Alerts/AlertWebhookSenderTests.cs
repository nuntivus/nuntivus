using NuntivusServer.Application.Alerts;
using NuntivusServer.Application.Deliveries.Execution;
using NuntivusServer.Application.Security;
using NuntivusServer.Common;
using NuntivusServer.Infastructure.Alerts;

namespace NuntivusServer.UnitTests.Alerts
{
    public sealed class AlertWebhookSenderTests
    {
        [Theory]
        [InlineData("http://hooks.example.com/alert")]
        [InlineData("https://127.0.0.1/alert")]
        [InlineData("https://localhost/alert")]
        [InlineData("https://10.0.0.1/alert")]
        [InlineData("https://user:password@hooks.example.com/alert")]
        [InlineData("https://hooks.example.com/alert#fragment")]
        public async Task Unsafe_endpoints_are_rejected_before_credentials_or_network(string endpoint)
        {
            var resolver = new UnavailableSecrets();
            var sender = new AlertWebhookSender(resolver, new EndpointSecurityPolicy());
            var result = await sender.SendAsync(new AlertWebhookMessage(Guid.NewGuid(), Guid.NewGuid(), endpoint, "secret.alert", "{}", 1), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal(0, resolver.Calls);
        }

        [Fact]
        public async Task Missing_secret_stops_delivery_without_exposing_provider_error()
        {
            var resolver = new UnavailableSecrets();
            var sender = new AlertWebhookSender(resolver, new EndpointSecurityPolicy());
            var result = await sender.SendAsync(new AlertWebhookMessage(Guid.NewGuid(), Guid.NewGuid(), "https://hooks.example.com/alert", "secret.alert", "{}", 1), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal(1, resolver.Calls);
            Assert.DoesNotContain("sensitive", result.Error!.Message);
        }

        private sealed class UnavailableSecrets : ISecretResolver
        {
            public int Calls { get; private set; }
            public Task<Result<string>> ResolveAsync(string secretRef, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Result.Failure<string>(Error.Validation("sensitive provider error")));
            }
        }
    }
}
