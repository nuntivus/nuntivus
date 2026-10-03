using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Worker.Sources
{
    public sealed class SourceEventProcessor : ISourceEventProcessor
    {
        private readonly ISourceIngestionService _ingestion;
        private readonly ISourceIngestionStateRepository _state;

        public SourceEventProcessor(ISourceIngestionService ingestion, ISourceIngestionStateRepository state)
        {
            _ingestion = ingestion;
            _state = state;
        }

        public async Task<Result> ProcessAsync(SourceType type, SourceLease lease, string body,
            string stableKey, CancellationToken cancellationToken)
        {
            var result = await _ingestion.IngestAsync(type, lease.SourceId,
                new InboundSourceEventContext(lease.SourceId, body, stableKey), cancellationToken);
            if (result.IsFailure) return Result.Failure(result.Errors);
            var marked = await _state.MarkProcessedAsync(lease, stableKey, cancellationToken);
            if (marked.IsFailure || !marked.Value)
                return Result.Failure(Error.Conflict("Source ownership was lost before progress was recorded."));
            return Result.Success();
        }
    }
}
