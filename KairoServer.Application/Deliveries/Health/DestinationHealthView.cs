using System;

namespace KairoServer.Application.Deliveries.Health
{
    public sealed record DestinationHealthView(
        Guid ContractId,
        string CustomerId,
        string DestinationType,
        int ConsecutiveFailures,
        DateTimeOffset? LastSuccessAt,
        DateTimeOffset? LastFailureAt,
        DateTimeOffset LastAttemptAt,
        double? LastLatencyMs,
        double? AverageLatencyMs,
        long LatencySampleCount,
        DestinationHealthStatus Status,
        DateTimeOffset UpdatedAt);
}
