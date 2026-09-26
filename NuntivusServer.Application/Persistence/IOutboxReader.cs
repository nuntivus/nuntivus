using NuntivusServer.Common;
using NuntivusServer.Domain.Outbox;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Application.Persistence
{
    public interface IOutboxReader
    {
        Task<Result<IReadOnlyList<OutboxMessage>>> GetPendingAsync(
            int batchSize,
            string workerId,
            TimeSpan claimTtl,
            CancellationToken cancellationToken);

        Task<Result> MarkProcessedAsync(
            Guid id,
            string workerId,
            CancellationToken cancellationToken);

        Task<Result> ReleaseClaimAfterFailureAsync(
            Guid id,
            string workerId,
            DateTimeOffset failedAt,
            DateTimeOffset nextAttemptAt,
            string error,
            CancellationToken cancellationToken);

        Task<Result<int>> ReleaseExpiredClaimsAsync(
            string workerId,
            CancellationToken cancellationToken);
    }
}
