using System;

namespace NuntivusServer.Application.Persistence
{
    public sealed record EventReplay(
        Guid SourceEventId,
        string ReplayKey,
        Guid ReplayedEventId,
        DateTimeOffset CreatedAt);
}
