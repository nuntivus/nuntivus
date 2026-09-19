using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Persistence
{
    public interface IRetentionCleanupRepository
    {
        Task<Result<int>> DeleteProcessedOutboxBatchAsync(
            DateTimeOffset processedBefore,
            int batchSize,
            CancellationToken cancellationToken);

        Task<Result<int>> DeleteDeliveryAttemptsBatchAsync(
            DateTimeOffset completedBefore,
            int batchSize,
            CancellationToken cancellationToken);

        Task<Result<int>> DeleteDeliveriesBatchAsync(
            DateTimeOffset updatedBefore,
            int batchSize,
            CancellationToken cancellationToken);

        Task<Result<int>> DeleteEventsBatchAsync(
            DateTimeOffset receivedBefore,
            int batchSize,
            CancellationToken cancellationToken);

        Task<Result<int>> DeleteAuditLogsBatchAsync(
            DateTimeOffset createdBefore,
            int batchSize,
            CancellationToken cancellationToken);
    }
}
