using NuntivusServer.Common;

namespace NuntivusServer.Application.Contracts
{
    public interface ISloObservationRetention
    {
        Task<Result<int>> DeleteExpiredBatchAsync(CancellationToken cancellationToken);
    }
}
