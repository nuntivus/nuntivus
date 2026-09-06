using System.Text.Json;

namespace KairoServer.Api.Controllers.Events
{
    public sealed record PublishEventRequest(
        string IdempotencyKey,
        string Type,
        string Source,
        DateTimeOffset Timestamp,
        JsonElement? Metadata,
        JsonElement Payload);
}
