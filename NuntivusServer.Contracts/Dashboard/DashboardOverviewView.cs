using System;

namespace NuntivusServer.Contracts.Dashboard
{
    public sealed record DashboardOverviewView(
        DateTimeOffset AsOf,
        long TotalCustomers,
        long TotalPendingDeliveries,
        long TotalFailedDeliveries,
        long TotalDeadDeliveries);
}
