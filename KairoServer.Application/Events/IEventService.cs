using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;

namespace KairoServer.Application.Events
{
    public interface IEventService
    {
        Task<Result<Guid>> PublishAsync(
            string idempotencyKey,
            string type,
            string sourceId,
            string payload,
            DateTimeOffset occurredAt,
            string? metadata,
            CancellationToken cancellationToken);
    }
}
