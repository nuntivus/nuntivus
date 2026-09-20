using System;

namespace KairoServer.Contracts.Dashboard
{
    public sealed record FailedDeliveryView(
        Guid DeliveryId,
        Guid EventId,
        Guid ContractId,
        string CustomerId,
        string EventType,
        int AttemptCount,
        string? LastError,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
