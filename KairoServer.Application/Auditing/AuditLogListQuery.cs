using System;

namespace KairoServer.Application.Auditing
{
    public sealed record AuditLogListQuery(
        string? Category,
        string? Action,
        string? ActorId,
        string? CustomerId,
        Guid? ContractId,
        Guid? DeliveryId,
        Guid? EventId,
        Guid? OperationId,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        string? SortDirection,
        int Page,
        int PageSize);
}
