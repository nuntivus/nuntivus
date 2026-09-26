using System;

namespace NuntivusServer.Application.Sources
{
    public sealed record HttpApiSourceEventContext(
        string IdempotencyKey,
        string Type,
        string Source,
        DateTimeOffset Timestamp,
        string Payload,
        string? Metadata);
}
