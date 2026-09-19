using System;
using System.Collections.Generic;
using System.Text.Json;
using KairoServer.Application.Deliveries.Queries;

namespace KairoServer.Api.Controllers.Events
{
    public sealed record EventQueryDto(
        Guid Id,
        string IdempotencyKey,
        string Type,
        string Source,
        DateTimeOffset OccurredAt,
        DateTimeOffset ReceivedAt,
        JsonElement Payload,
        JsonElement? Metadata,
        IReadOnlyList<DeliveryQueryView> Deliveries);
}
