using System;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Recovery
{
    public sealed record DeliveryRecoveryView(
        Guid Id,
        DeliveryStatus Status,
        int AttemptCount,
        string? LastError,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset UpdatedAt);
}
