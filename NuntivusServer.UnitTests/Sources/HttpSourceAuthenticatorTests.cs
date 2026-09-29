using NuntivusServer.Application.Contracts.Destinations;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.UnitTests.Sources
{
    public sealed class HttpSourceAuthenticatorTests
    {
        [Fact]
        public async Task TokenAuthenticationRejectsMissingAndInvalidTokensWithoutRevealingSecret()
        {
            var source = Source.Create(Guid.NewGuid().ToString("D"), "Orders", SourceType.HTTP_API,
                "{\"authentication\":{\"type\":\"token\",\"secretRef\":\"secret.orders.ingest\"}}", true);
            var sut = new HttpSourceAuthenticator(new FakeSecrets(), new FakeProtector());

            var missing = await sut.AuthenticateAsync(source, null, CancellationToken.None);
            var invalid = await sut.AuthenticateAsync(source, "incorrect", CancellationToken.None);
            var valid = await sut.AuthenticateAsync(source, "private-token", CancellationToken.None);

            Assert.Equal("unauthorized", missing.Error?.Code);
            Assert.Equal("unauthorized", invalid.Error?.Code);
            Assert.DoesNotContain("private-token", invalid.Error!.Message);
            Assert.True(valid.IsSuccess);
        }

        private sealed class FakeProtector : IDestinationCredentialProtector
        {
            public Result<string> Protect(string value) => Result.Success(value);
            public Result<string> Unprotect(string value) => Result.Success(value);
        }

        private sealed class FakeSecrets : IManagedSecretRepository
        {
            public Task<string?> GetEncryptedValueAsync(string name, CancellationToken ct) =>
                Task.FromResult<string?>(name == "secret.orders.ingest" ? "private-token" : null);
            public Task<IReadOnlyList<ManagedSecret>> ListAsync(CancellationToken ct) => throw new NotImplementedException();
            public Task<bool> CreateAsync(string name, string encryptedValue, CancellationToken ct) => throw new NotImplementedException();
            public Task<int?> AddVersionAsync(string name, string encryptedValue, CancellationToken ct) => throw new NotImplementedException();
            public Task<IReadOnlyList<ManagedSecretVersion>?> ListVersionsAsync(string name, CancellationToken ct) => throw new NotImplementedException();
            public Task<ManagedSecretDeleteResult> DeleteAsync(string name, CancellationToken ct) => throw new NotImplementedException();
        }
    }
}
