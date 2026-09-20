using System;
using KairoServer.Domain.Sources;

namespace KairoServer.Application.Sources
{
    public sealed record CanonicalSourceEvent(
        string IdempotencyKey,
        string Type,
        SourceId SourceId,
        DateTimeOffset OccurredAt,
        string Payload,
        string? Metadata);
}
