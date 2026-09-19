using System;

namespace KairoServer.Api.Controllers.Deliveries
{
    public sealed record ManualDeliveryOperationApiRequest(Guid OperationId, string? Reason);
}
