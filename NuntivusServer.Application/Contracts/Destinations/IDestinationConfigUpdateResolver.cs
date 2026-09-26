using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Contracts.Destinations
{
    public interface IDestinationConfigUpdateResolver
    {
        Result<DestinationConfig> Resolve(DestinationConfig requested, Destination current);
    }
}
