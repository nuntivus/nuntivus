using System;

namespace NuntivusServer.Application.Contracts
{
    public sealed record AdminContractListQuery(
        string? CustomerId,
        bool? Enabled,
        string? DestinationType,
        string? EventType,
        string? Search,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        string? SortBy,
        string? SortDirection,
        int Page,
        int PageSize);
}
