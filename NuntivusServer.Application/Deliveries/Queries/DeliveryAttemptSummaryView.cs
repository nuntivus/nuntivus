using System;

namespace NuntivusServer.Application.Deliveries.Queries
{
    public sealed record DeliveryAttemptSummaryView(
        int TotalAttempts,
        int SuccessfulAttempts,
        int FailedAttempts,
        DateTimeOffset? LastAttemptAt);
}
