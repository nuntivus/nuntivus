using System;

namespace NuntivusServer.Application.Deliveries.Recovery
{
    public sealed record ManualDeliveryOperationRequest(
        Guid OperationId,
        string OperatorId,
        string? Reason,
        string RequestSnapshot);
}
