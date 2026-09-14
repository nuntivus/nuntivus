using System;

namespace KairoServer.Application.Persistence
{
    public sealed record EventReplay(
        Guid SourceEventId,
        string ReplayKey,
        Guid ReplayedEventId,
        DateTimeOffset CreatedAt);
}
