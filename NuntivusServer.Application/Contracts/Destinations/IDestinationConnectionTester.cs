using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Application.Contracts.Destinations
{
    public interface IDestinationConnectionTester
    {
        DestinationType Type { get; }
        Task<Result<string>> TestAsync(Destination destination, CancellationToken cancellationToken);
        Task<Result<string>> TestAsync(Destination destination, string payload, CancellationToken cancellationToken) =>
            TestAsync(destination, cancellationToken);
    }
}
