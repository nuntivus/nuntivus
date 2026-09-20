using System;

namespace KairoServer.Application.Contracts
{
    public sealed record ContractListQuery(
        string CustomerId,
        bool? Enabled,
        string? EventType,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        string? SortBy,
        string? SortDirection,
        int Page,
        int PageSize);
}
