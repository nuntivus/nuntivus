using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Events;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class SourceIngestionService : ISourceIngestionService
    {
        private readonly ISourceAdapterRegistry _adapterRegistry;
        private readonly IEventService _events;
        private readonly ISourceRepository _sources;

        public SourceIngestionService(
            ISourceAdapterRegistry adapterRegistry,
            IEventService events,
            ISourceRepository sources)
        {
            _adapterRegistry = adapterRegistry;
            _events = events;
            _sources = sources;
        }

        public async Task<Result<SourceIngestionResult>> IngestAsync(
            SourceType sourceType,
            string sourceId,
            object adapterContext,
            CancellationToken cancellationToken)
        {
            if (!System.Guid.TryParse(sourceId, out var id))
                return Result.Failure<SourceIngestionResult>(Error.Validation("Source id must be a GUID."));

            var sourceResult = await _sources.GetByIdAsync(id.ToString("D"), cancellationToken);
            if (sourceResult.IsFailure)
                return Result<SourceIngestionResult>.Fail(sourceResult.Errors);
            var source = sourceResult.Value;
            if (source is null)
                return Result.Failure<SourceIngestionResult>(Error.NotFound("Source was not found."));
            if (!source.Enabled)
                return Result.Failure<SourceIngestionResult>(Error.Conflict("Source is disabled."));
            if (source.Type != sourceType)
                return Result.Failure<SourceIngestionResult>(Error.Validation("Source type does not match the ingestion adapter."));

            var adapterResult = _adapterRegistry.Get(sourceType);
            if (adapterResult.IsFailure)
                return Result<SourceIngestionResult>.Fail(adapterResult.Errors);

            var adapter = adapterResult.EnsureValue();
            var normalizeResult = await adapter.NormalizeAsync(adapterContext, cancellationToken);
            if (normalizeResult.IsFailure)
                return Result<SourceIngestionResult>.Fail(normalizeResult.Errors);

            var canonical = normalizeResult.EnsureValue();
            if (canonical.SourceId != source.Id)
                return Result.Failure<SourceIngestionResult>(Error.Validation("Event source does not match the registered Source."));

            var publishResult = await _events.PublishAsync(
                canonical.IdempotencyKey,
                canonical.Type,
                canonical.SourceId.Value,
                canonical.Payload,
                canonical.OccurredAt,
                canonical.Metadata,
                cancellationToken);

            if (publishResult.IsFailure)
                return Result<SourceIngestionResult>.Fail(publishResult.Errors);

            var eventId = publishResult.EnsureValue();
            return Result.Success(new SourceIngestionResult(
                eventId.ToString("D"),
                "accepted",
                canonical.SourceId.Value));
        }
    }
}
