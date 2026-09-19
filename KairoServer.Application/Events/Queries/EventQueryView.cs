using System;
using System.Collections.Generic;
using KairoServer.Application.Deliveries.Queries;

namespace KairoServer.Application.Events.Queries
{
    public sealed record EventQueryView(
        Guid Id,
        string IdempotencyKey,
        string Type,
        string Source,
        DateTimeOffset OccurredAt,
        DateTimeOffset ReceivedAt,
        string Payload,
        string? Metadata,
        IReadOnlyList<DeliveryQueryView> Deliveries);
}
