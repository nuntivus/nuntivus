using System;

namespace NuntivusServer.Contracts.Deliveries
{
    /// <summary>
    /// Destination-neutral work message signalling that a persisted Delivery
    /// row is ready to be dispatched. Consumers look up destination details
    /// from the database at process time so the wire contract stays stable
    /// across destination configuration changes.
    /// </summary>
    public sealed record DeliveryReady(
        Guid DeliveryId,
        Guid EventId,
        Guid ContractId,
        string CustomerId,
        string EventType,
        string Payload,
        DateTimeOffset OccurredAt);
}
