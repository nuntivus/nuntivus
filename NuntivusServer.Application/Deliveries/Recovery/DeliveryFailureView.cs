using System;
using NuntivusServer.Domain.Deliveries;

namespace NuntivusServer.Application.Deliveries.Recovery
{
    public sealed record DeliveryFailureView(
        Guid DeliveryId,
        DeliveryStatus Status,
        string? LastError,
        string? FailureCategory,
        DateTimeOffset? FailedAt,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset UpdatedAt);
}
