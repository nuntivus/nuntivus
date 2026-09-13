using KairoServer.Common;
using KairoServer.Contracts.CustomerContracts;
using KairoServer.Domain.Contracts;

namespace KairoServer.Application.Contracts.Destinations
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
    }
}
