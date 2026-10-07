using System;

namespace NuntivusServer.Domain.Contracts
{
    public sealed record DestinationResource(
        Guid Id,
        string Name,
        string? Description,
        Destination Destination,
        bool Enabled,
        string DisablePolicy,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
