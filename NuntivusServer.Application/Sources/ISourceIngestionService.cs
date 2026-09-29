using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public interface ISourceIngestionService
    {
        Task<Result<SourceIngestionResult>> IngestAsync(
            SourceType sourceType,
            string sourceId,
            object adapterContext,
            CancellationToken cancellationToken);
    }

    public sealed record SourceIngestionResult(
        string EventId,
        string Status,
        string SourceId);
}
