using System;
using NuntivusServer.Domain.Deliveries;

namespace NuntivusServer.Application.Deliveries.Recovery
{
    public sealed record DeliveryRecoveryView(
        Guid Id,
        DeliveryStatus Status,
        int AttemptCount,
        string? LastError,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset UpdatedAt);
}
