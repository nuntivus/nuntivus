using NuntivusServer.Domain.Sources;
using NuntivusServer.Application.Persistence;

namespace NuntivusServer.Worker.Sources
{
    public interface ISourceRunner
    {
        SourceType Type { get; }
        Task RunAsync(Source source, SourceLease lease, CancellationToken cancellationToken);
    }
}
