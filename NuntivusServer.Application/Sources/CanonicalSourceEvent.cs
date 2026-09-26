using System;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed record CanonicalSourceEvent(
        string IdempotencyKey,
        string Type,
        SourceId SourceId,
        DateTimeOffset OccurredAt,
        string Payload,
        string? Metadata);
}
