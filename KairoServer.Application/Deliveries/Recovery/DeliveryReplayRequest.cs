using System;

namespace KairoServer.Application.Deliveries.Recovery
{
    public sealed record DeliveryReplayRequest(
        Guid OperationId,
        string OperatorId,
        string RequestSnapshot,
        string? EventType,
        string? CustomerId,
        Guid? ContractId,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        int BatchLimit);
}
