using System;
using KairoServer.Application.Events;
using KairoServer.Application.Sources;
using KairoServer.Common;
using KairoServer.Domain.Sources;

namespace KairoServer.UnitTests.Sources
{
    public sealed class SourceIngestionServiceTests
    {
        [Fact]
        public async Task IngestAsync_WithRegisteredAdapter_PublishesCanonicalEvent()
        {
            var source = Guid.NewGuid().ToString("D").ToUpperInvariant();
            var eventService = new FakeEventService();
            var sut = new SourceIngestionService(
                new SourceAdapterRegistry(new ISourceAdapter[] { new HttpApiSourceAdapter() }),
                eventService);

            var result = await sut.IngestAsync(
                SourceType.HTTP_API,
                new HttpApiSourceEventContext(
                    IdempotencyKey: " idem-1 ",
                    Type: " orders.created ",
                    Source: source,
                    Timestamp: DateTimeOffset.Parse("2026-01-01T10:00:00Z"),
                    Payload: "{\"id\":\"1\"}",
                    Metadata: null),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(source.ToLowerInvariant(), eventService.LastPublishedSourceId);
            Assert.Equal("idem-1", eventService.LastPublishedIdempotencyKey);
            Assert.Equal("orders.created", eventService.LastPublishedType);
            Assert.Equal("accepted", result.EnsureValue().Status);
        }

        [Fact]
        public async Task IngestAsync_WithoutRegisteredAdapter_ReturnsNotFound()
        {
            var sut = new SourceIngestionService(
                new SourceAdapterRegistry(Array.Empty<ISourceAdapter>()),
                new FakeEventService());

            var result = await sut.IngestAsync(
                SourceType.KAFKA,
                new object(),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("not_found", result.Error?.Code);
        }

        private sealed class FakeEventService : IEventService
        {
            public string? LastPublishedIdempotencyKey { get; private set; }
            public string? LastPublishedType { get; private set; }
            public string? LastPublishedSourceId { get; private set; }

            public Task<Result<Guid>> PublishAsync(
                string idempotencyKey,
                string type,
                string sourceId,
                string payload,
                DateTimeOffset occurredAt,
                string? metadata,
                CancellationToken cancellationToken)
            {
                LastPublishedIdempotencyKey = idempotencyKey;
                LastPublishedType = type;
                LastPublishedSourceId = sourceId;
                return Task.FromResult(Result.Success(Guid.Parse("6dcb4dc4-2f4e-48d2-8444-9c5486a2f790")));
            }

            public Task<Result<ReplayEventResult>> ReplayAsync(
                Guid eventId,
                string replayKey,
                string? reason,
                CancellationToken cancellationToken,
                string? actorId = null)
            {
                throw new NotImplementedException();
            }
        }
    }
}
