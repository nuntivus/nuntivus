using System;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Recovery
{
    public sealed record DeliveryFailureView(
        Guid DeliveryId,
        DeliveryStatus Status,
        string? LastError,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset UpdatedAt);
}
