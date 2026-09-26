using System;

namespace NuntivusServer.Application.Sources
{
    public sealed record GenericWebhookSourceEventContext(
        string Source,
        string IdempotencyKey,
        string Type,
        DateTimeOffset Timestamp,
        string Payload,
        string? Metadata);
}
