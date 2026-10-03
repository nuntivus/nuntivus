using NuntivusServer.Common;

namespace NuntivusServer.Application.Persistence
{
    public sealed record SourceLease(string SourceId, string OwnerId, long Epoch);

    public interface ISourceIngestionStateRepository
    {
        Task<Result<SourceLease?>> TryAcquireAsync(string sourceId, string ownerId, TimeSpan duration, CancellationToken cancellationToken);
        Task<Result<bool>> RenewAsync(SourceLease lease, TimeSpan duration, CancellationToken cancellationToken);
        Task<Result> ReleaseAsync(SourceLease lease, CancellationToken cancellationToken);
        Task<Result<bool>> HasProcessedAsync(string sourceId, string itemKey, CancellationToken cancellationToken);
        Task<Result<bool>> MarkProcessedAsync(SourceLease lease, string itemKey, CancellationToken cancellationToken);
    }
}
