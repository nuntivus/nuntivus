using System;

namespace NuntivusServer.Application.Customers
{
    public sealed record CustomerListQuery(
        bool? Enabled,
        string? Search,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        string? SortBy,
        string? SortDirection,
        int Page,
        int PageSize);
}
