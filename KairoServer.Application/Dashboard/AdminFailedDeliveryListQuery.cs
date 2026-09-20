using System;

namespace KairoServer.Application.Dashboard
{
    public sealed record AdminFailedDeliveryListQuery(
        Guid? EventId,
        string? CustomerId,
        Guid? ContractId,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        string? SortBy,
        string? SortDirection,
        int Page,
        int PageSize);
}
