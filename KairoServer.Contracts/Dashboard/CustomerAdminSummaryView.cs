using System;

namespace KairoServer.Contracts.Dashboard
{
    public sealed record CustomerAdminSummaryView(
        string CustomerId,
        string CustomerName,
        bool CustomerEnabled,
        long TotalContracts,
        long EnabledContracts,
        long DisabledContracts,
        int HealthyDestinations,
        int DegradedDestinations,
        int UnhealthyDestinations,
        DateTimeOffset AsOf);
}
