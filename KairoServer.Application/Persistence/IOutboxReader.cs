using KairoServer.Common;
using KairoServer.Domain.Outbox;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KairoServer.Application.Persistence
{
    public interface IOutboxReader
    {
        Task<Result<IReadOnlyList<OutboxMessage>>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken);

        Task<Result> MarkProcessedAsync(
            Guid id,
            CancellationToken cancellationToken);

        Task<Result> IncrementAttemptAsync(
            Guid id,
            CancellationToken cancellationToken);
    }
}
