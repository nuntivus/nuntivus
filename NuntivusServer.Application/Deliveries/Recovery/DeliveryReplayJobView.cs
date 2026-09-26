using System;

namespace NuntivusServer.Application.Deliveries.Recovery
{
    public sealed record DeliveryReplayJobView(
        Guid Id,
        Guid OperationId,
        string OperatorId,
        DeliveryReplayJobStatus Status,
        string? EventType,
        string? CustomerId,
        Guid? ContractId,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        int BatchLimit,
        int ReplayedCount,
        string? Error,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? CompletedAt);
}
