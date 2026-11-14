using System;
using NuntivusServer.Domain.Deliveries;

namespace NuntivusServer.Application.Deliveries.Queries
{
    public sealed record DeliveryQueryView(
        Guid Id,
        Guid EventId,
        string CustomerId,
        Guid ContractId,
        string DestinationType,
        string EventType,
        DeliveryStatus Status,
        int AttemptCount,
        string? LastError,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DeliveryAttemptSummaryView AttemptSummary,
        DeliveryLatestFailureView? LatestFailure,
        string? TransformationIdentity = null,
        Guid? DestinationId = null);
}
