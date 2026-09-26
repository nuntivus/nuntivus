using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Events;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.Application.Sources
{
    public sealed class SourceIngestionService : ISourceIngestionService
    {
        private readonly ISourceAdapterRegistry _adapterRegistry;
        private readonly IEventService _events;

        public SourceIngestionService(
            ISourceAdapterRegistry adapterRegistry,
            IEventService events)
        {
            _adapterRegistry = adapterRegistry;
            _events = events;
        }

        public async Task<Result<SourceIngestionResult>> IngestAsync(
            SourceType sourceType,
            object adapterContext,
            CancellationToken cancellationToken)
        {
            var adapterResult = _adapterRegistry.Get(sourceType);
            if (adapterResult.IsFailure)
                return Result<SourceIngestionResult>.Fail(adapterResult.Errors);

            var adapter = adapterResult.EnsureValue();
            var normalizeResult = await adapter.NormalizeAsync(adapterContext, cancellationToken);
            if (normalizeResult.IsFailure)
                return Result<SourceIngestionResult>.Fail(normalizeResult.Errors);

            var canonical = normalizeResult.EnsureValue();

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
