using System;
using System.Text.Json;

namespace KairoServer.Api.Controllers.Sources
{
    public sealed record WebhookIngestRequest(
        string? IdempotencyKey,
        string? Type,
        DateTimeOffset? Timestamp,
        JsonElement? Metadata,
        JsonElement Payload);
}
