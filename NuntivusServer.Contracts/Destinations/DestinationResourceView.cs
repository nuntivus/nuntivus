using System;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Contracts.Destinations
{
    public sealed record DestinationResourceView(Guid Id, string Name, string? Description,
        DestinationType Type, string Configuration, bool Enabled, string DisablePolicy,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public sealed record CreateDestinationRequest(string Name, string? Description, DestinationConfig Configuration);
    public sealed record UpdateDestinationRequest(string Name, string? Description, DestinationConfig Configuration);
    public sealed record DisableDestinationRequest(bool PauseExistingBindings);
    public sealed record DeleteDestinationRequest(bool RemoveBindings);
    public sealed record ContractDestinationView(Guid Id, Guid? DestinationId, DestinationType Type,
        string Configuration, bool DeliveryEnabled);
}
