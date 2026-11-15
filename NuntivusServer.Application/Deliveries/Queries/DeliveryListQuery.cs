using System;
using NuntivusServer.Domain.Deliveries;

namespace NuntivusServer.Application.Deliveries.Queries
{
    public sealed record DeliveryListQuery(
        Guid? EventId,
        string? CustomerId,
        Guid? ContractId,
        string? DestinationType,
        string? FailureCategory,
        DeliveryStatus? Status,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        string? SortBy,
        string? SortDirection,
        int Page,
        int PageSize,
        string? Search = null,
        bool FailuresOnly = false);
}
