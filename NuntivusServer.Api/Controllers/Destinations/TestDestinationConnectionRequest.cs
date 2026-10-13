using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Api.Controllers.Destinations
{
    public sealed record TestDestinationConnectionRequest(DestinationConfig Configuration);
}
