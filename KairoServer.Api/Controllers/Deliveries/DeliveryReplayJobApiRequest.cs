using System;

namespace KairoServer.Api.Controllers.Deliveries
{
    public sealed record DeliveryReplayJobApiRequest(
        Guid OperationId,
        string? EventType,
        string? CustomerId,
        Guid? ContractId,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        int BatchLimit,
        string? Reason);
}
