using System;

namespace KairoServer.Application.Deliveries.Health
{
    public sealed record DestinationHealthObservation(
        Guid ContractId,
        string CustomerId,
        string DestinationType,
        bool Succeeded,
        DateTimeOffset AttemptedAt,
        double? LatencyMs);
}
