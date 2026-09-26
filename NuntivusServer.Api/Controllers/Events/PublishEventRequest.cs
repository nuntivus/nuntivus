using System.Text.Json;

namespace NuntivusServer.Api.Controllers.Events
{
    public sealed record PublishEventRequest(
        string IdempotencyKey,
        string Type,
        string Source,
        DateTimeOffset Timestamp,
        JsonElement? Metadata,
        JsonElement Payload);
}
