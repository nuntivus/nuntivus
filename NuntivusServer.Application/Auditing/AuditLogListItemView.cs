using System;

namespace NuntivusServer.Application.Auditing
{
    public sealed record AuditLogListItemView(
        Guid Id,
        string Category,
        string Action,
        string? ActorId,
        string? CustomerId,
        Guid? ContractId,
        Guid? DeliveryId,
        Guid? EventId,
        Guid? OperationId,
        string Details,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);
}
