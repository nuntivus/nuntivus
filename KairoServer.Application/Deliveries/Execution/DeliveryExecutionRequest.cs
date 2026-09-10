using System;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Deliveries.Execution
{
    /// <summary>
    /// Protocol-neutral input passed by Delivery Core to a resolved
    /// <see cref="IDeliveryExecutor"/>. Carries only fields any transport
    /// needs; the opaque <see cref="Destination"/> configuration is parsed
    /// by the executor implementation.
    /// </summary>
    public sealed record DeliveryExecutionRequest(
        Guid DeliveryId,
        Guid EventId,
        Guid ContractId,
        string CustomerId,
        string EventType,
        string Payload,
        int AttemptNumber,
        Destination Destination);
}
