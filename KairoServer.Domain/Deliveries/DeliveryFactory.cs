using System;
using KairoServer.Domain.Contracts;

namespace KairoServer.Domain.Deliveries
{
    public sealed class DeliveryFactory : IDeliveryFactory
    {
        public Delivery CreatePending(
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            Destination destination,
            DateTimeOffset now,
            string? transformationConfiguration = null,
            ContractRateLimit? rateLimit = null,
            ContractOrdering? ordering = null) =>
            Delivery.CreatePending(
                eventId,
                contractId,
                customerId,
                eventType,
                payload,
                destination,
                now,
                transformationConfiguration,
                rateLimit,
                ordering);
    }
}
