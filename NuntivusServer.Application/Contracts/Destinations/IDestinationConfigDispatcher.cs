using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Contracts.Destinations
{
    /// <summary>
    /// Dispatches a <see cref="DestinationConfig"/> to the registered
    /// <see cref="IDestinationConfigHandler"/> matching its runtime type
    /// and returns the resulting domain <see cref="Destination"/>.
    /// </summary>
    public interface IDestinationConfigDispatcher
    {
        Result<Destination> Build(DestinationConfig? configuration);

        Result<string> Redact(Destination destination);

        Result<Destination> Rotate(Destination current, Destination next);
    }
}
