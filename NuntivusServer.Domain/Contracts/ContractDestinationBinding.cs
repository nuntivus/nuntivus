using System;

namespace NuntivusServer.Domain.Contracts
{
    public sealed record ContractDestinationBinding(
        Guid Id,
        Guid? DestinationId,
        Destination Destination,
        bool DeliveryEnabled,
        DateTimeOffset CreatedAt,
        string? AuthenticationReference = null,
        int? AuthenticationVersion = null);
}
