using System.Threading;
using System.Threading.Tasks;
using KairoServer.Common;
using KairoServer.Domain.Sources;

namespace KairoServer.Application.Sources
{
    public interface ISourceIngestionService
    {
        Task<Result<SourceIngestionResult>> IngestAsync(
            SourceType sourceType,
            object adapterContext,
            CancellationToken cancellationToken);
    }

    public sealed record SourceIngestionResult(
        string EventId,
        string Status,
        string SourceId);
}
