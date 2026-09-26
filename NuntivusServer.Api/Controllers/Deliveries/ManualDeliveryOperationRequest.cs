using System;

namespace NuntivusServer.Api.Controllers.Deliveries
{
    public sealed record ManualDeliveryOperationApiRequest(Guid OperationId, string? Reason);
}
