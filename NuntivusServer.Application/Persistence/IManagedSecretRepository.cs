using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Application.Persistence
{
    public sealed record ManagedSecret(string Name, DateTimeOffset CreatedAt, int CurrentVersion);
    public sealed record ManagedSecretVersion(int Version, DateTimeOffset CreatedAt);

    public enum ManagedSecretDeleteResult
    {
        Deleted,
        Referenced,
        NotFound
    }

    public interface IManagedSecretRepository
    {
        Task<IReadOnlyList<ManagedSecret>> ListAsync(CancellationToken cancellationToken);
        Task<bool> CreateAsync(string name, string encryptedValue, CancellationToken cancellationToken);
        Task<int?> AddVersionAsync(string name, string encryptedValue, CancellationToken cancellationToken);
        Task<IReadOnlyList<ManagedSecretVersion>?> ListVersionsAsync(string name, CancellationToken cancellationToken);
        Task<ManagedSecretDeleteResult> DeleteAsync(string name, CancellationToken cancellationToken);
        Task<string?> GetEncryptedValueAsync(string name, CancellationToken cancellationToken);
    }
}
