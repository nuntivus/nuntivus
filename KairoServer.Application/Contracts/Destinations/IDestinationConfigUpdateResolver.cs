using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Contracts.Destinations
{
    public interface IDestinationConfigUpdateResolver
    {
        Result<DestinationConfig> Resolve(DestinationConfig requested, Destination current);
    }
}
