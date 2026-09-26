using System;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Contracts.Dashboard
{
    public sealed record ContractAdminSummaryView(
        Guid ContractId,
        string CustomerId,
        string? ContractName,
        bool ContractEnabled,
        DestinationType DestinationType,
        int EventTypeCount,
        long PendingDeliveries,
        long InFlightDeliveries,
        long DeliveredDeliveries,
        long FailedDeliveries,
        long DeadDeliveries,
        ContractDestinationHealthSnapshotView? DestinationHealth,
        DateTimeOffset AsOf);

    public sealed record ContractDestinationHealthSnapshotView(
        string DestinationType,
        int ConsecutiveFailures,
        DateTimeOffset? LastSuccessAt,
        DateTimeOffset? LastFailureAt,
        DateTimeOffset LastAttemptAt,
        string Status,
        DateTimeOffset UpdatedAt);
}
