using System.Security.Cryptography;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Infastructure.Deliveries.Http;

namespace NuntivusServer.UnitTests.Deliveries.Execution
{
    public sealed class ProtectedSecretResolverTests
    {
        [Fact]
        public async Task ResolveAsync_decrypts_stored_secret_reference()
        {
            var protector = new AesGcmDestinationCredentialProtector(new DestinationCredentialOption
            {
                EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            });
            var repository = new FakeSecretRepository();
            await repository.CreateAsync("secret.acme.webhook", protector.Protect("credential").Value, CancellationToken.None);
            var resolver = new ProtectedSecretResolver(protector, repository);

            var result = await resolver.ResolveAsync("secret.acme.webhook", CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("credential", result.Value);
        }

        [Fact]
        public async Task ResolveAsync_rejects_missing_reference()
        {
            var protector = new AesGcmDestinationCredentialProtector(new DestinationCredentialOption
            {
                EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            });
            var resolver = new ProtectedSecretResolver(protector, new FakeSecretRepository());

            var result = await resolver.ResolveAsync("secret.missing", CancellationToken.None);

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task ResolveAsync_uses_latest_version_without_changing_reference()
        {
            var protector = new AesGcmDestinationCredentialProtector(new DestinationCredentialOption
            {
                EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            });
            var repository = new FakeSecretRepository();
            await repository.CreateAsync("secret.acme.webhook", protector.Protect("first").Value, CancellationToken.None);
            var resolver = new ProtectedSecretResolver(protector, repository);
            await repository.AddVersionAsync("secret.acme.webhook", protector.Protect("second").Value, CancellationToken.None);

            var resolved = await resolver.ResolveAsync("secret.acme.webhook", CancellationToken.None);

            Assert.True(resolved.IsSuccess);
            Assert.Equal("second", resolved.Value);
        }

        private sealed class FakeSecretRepository : IManagedSecretRepository
        {
            private readonly Dictionary<string, List<string>> _values = new();

            public Task<IReadOnlyList<ManagedSecret>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ManagedSecret>>(Array.Empty<ManagedSecret>());

            public Task<bool> CreateAsync(string name, string encryptedValue, CancellationToken cancellationToken) =>
                Task.FromResult(_values.TryAdd(name, new List<string> { encryptedValue }));

            public Task<int?> AddVersionAsync(string name, string encryptedValue, CancellationToken cancellationToken)
            {
                if (!_values.TryGetValue(name, out var versions)) return Task.FromResult<int?>(null);
                versions.Add(encryptedValue);
                return Task.FromResult<int?>(versions.Count);
            }

            public Task<IReadOnlyList<ManagedSecretVersion>?> ListVersionsAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ManagedSecretVersion>?>(null);

            public Task<ManagedSecretDeleteResult> DeleteAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(_values.Remove(name) ? ManagedSecretDeleteResult.Deleted : ManagedSecretDeleteResult.NotFound);

            public Task<string?> GetEncryptedValueAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(_values.TryGetValue(name, out var versions) ? versions[^1] : null);
        }
    }
}
