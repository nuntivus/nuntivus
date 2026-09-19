using System;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Queries
{
    public sealed record DeliveryQueryView(
        Guid Id,
        Guid EventId,
        string CustomerId,
        Guid ContractId,
        string EventType,
        DeliveryStatus Status,
        int AttemptCount,
        string? LastError,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DeliveryAttemptSummaryView AttemptSummary,
        DeliveryLatestFailureView? LatestFailure);
}
