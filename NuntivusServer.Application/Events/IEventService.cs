using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;

namespace NuntivusServer.Application.Events
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

        Task<Result<ReplayEventResult>> ReplayAsync(
            Guid eventId,
            string replayKey,
            string? reason,
            CancellationToken cancellationToken,
            string? actorId = null);
    }

    public sealed record ReplayEventResult(
        Guid SourceEventId,
        Guid ReplayedEventId,
        bool Created);
}
