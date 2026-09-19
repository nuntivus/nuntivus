using System;
using KairoServer.Domain.Deliveries;

namespace KairoServer.Application.Deliveries.Queries
{
    public sealed record DeliveryListQuery(
        Guid? EventId,
        string? CustomerId,
        Guid? ContractId,
        DeliveryStatus? Status,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        string? SortBy,
        string? SortDirection,
        int Page,
        int PageSize);
}
