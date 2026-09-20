using System;

namespace KairoServer.Contracts.Dashboard
{
    public sealed record DashboardOverviewView(
        DateTimeOffset AsOf,
        long TotalCustomers,
        long TotalPendingDeliveries,
        long TotalFailedDeliveries,
        long TotalDeadDeliveries);
}
