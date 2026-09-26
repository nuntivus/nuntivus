using System;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Domain.Deliveries
{
    public interface IDeliveryFactory
    {
        Delivery CreatePending(
            Guid eventId,
            Guid contractId,
            string customerId,
            string eventType,
            string payload,
            Destination destination,
            DateTimeOffset now,
            string? transformationConfiguration = null,
            ContractRateLimit? rateLimit = null,
            ContractOrdering? ordering = null,
            string? authenticationReference = null,
            int? authenticationVersion = null,
            int? transformationVersion = null,
            int? schemaVersion = null);
    }
}
