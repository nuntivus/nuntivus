using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Worker.Sources
{
    public interface ISourceEventProcessor
    {
        Task<Result> ProcessAsync(SourceType type, SourceLease lease, string body,
            string stableKey, CancellationToken cancellationToken);
    }
}
