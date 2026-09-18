using KairoServer.Application.Contracts.Destinations;
using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;

namespace KairoServer.UnitTests.Contracts.Destinations
{
    public sealed class HttpDestinationConfigHandlerSecurityTests
    {
        [Fact]
        public void Build_rejects_private_network_destination_by_default()
        {
            var handler = new HttpDestinationConfigHandler(new PassthroughProtector());

            var result = handler.Build(new HttpDestinationConfig(
                "http://localhost:8080/hook",
                new HttpAuthentication("none")));

            Assert.True(result.IsFailure);
            Assert.Contains("private-network", result.Errors[0].Message);
        }

        [Fact]
        public void Build_allows_private_network_destination_when_enabled()
        {
            var handler = new HttpDestinationConfigHandler(
                new PassthroughProtector(),
                new HttpDestinationSecurityOption
                {
                    AllowPrivateNetworkDestinations = true,
                });

            var result = handler.Build(new HttpDestinationConfig(
                "http://localhost:8080/hook",
                new HttpAuthentication("none")));

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void Build_rejects_overlong_inline_credential()
        {
            var handler = new HttpDestinationConfigHandler(new PassthroughProtector());
            var tooLong = new string('x', HttpDestinationConfigHandler.MaxCredentialValueLength + 1);

            var result = handler.Build(new HttpDestinationConfig(
                "https://hooks.example.com/acme",
                new HttpAuthentication("bearer", new HttpCredential("inline", tooLong))));

            Assert.True(result.IsFailure);
            Assert.Contains("exceeds maximum length", result.Errors[0].Message);
        }

        private sealed class PassthroughProtector : IDestinationCredentialProtector
        {
            public Result<string> Protect(string value) => Result.Success(value);

            public Result<string> Unprotect(string value) => Result.Success(value);
        }
    }
}
