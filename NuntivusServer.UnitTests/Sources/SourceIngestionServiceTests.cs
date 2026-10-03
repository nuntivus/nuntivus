using System;
using NuntivusServer.Application.Events;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Application.Sources;
using NuntivusServer.Common;
using NuntivusServer.Domain.Sources;

namespace NuntivusServer.UnitTests.Sources
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
                eventService,
                new FakeSourceRepository(Source.Create(source, "HTTP", SourceType.HTTP_API, "{\"authentication\":{\"type\":\"none\"}}", true)));

            var result = await sut.IngestAsync(
                SourceType.HTTP_API,
                source,
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
            var id = Guid.NewGuid().ToString("D");
            var sut = new SourceIngestionService(
                new SourceAdapterRegistry(Array.Empty<ISourceAdapter>()),
                new FakeEventService(),
                new FakeSourceRepository(Source.Create(id, "Kafka", SourceType.KAFKA, "{}", true)));

            var result = await sut.IngestAsync(
                SourceType.KAFKA,
                id,
                new object(),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("not_found", result.Error?.Code);
        }

        [Theory]
        [InlineData(false, "conflict")]
        [InlineData(true, "validation")]
        public async Task IngestAsync_RejectsDisabledOrWrongType(bool enabled, string expectedCode)
        {
            var id = Guid.NewGuid().ToString("D");
            var sut = new SourceIngestionService(new SourceAdapterRegistry([new HttpApiSourceAdapter()]),
                new FakeEventService(), new FakeSourceRepository(Source.Create(id, "Kafka", SourceType.KAFKA, "{}", enabled)));
            var result = await sut.IngestAsync(SourceType.HTTP_API, id, new object(), CancellationToken.None);
            Assert.Equal(expectedCode, result.Error?.Code);
        }

        [Fact]
        public async Task IngestAsync_RejectsMissingSource()
        {
            var sut = new SourceIngestionService(new SourceAdapterRegistry([new HttpApiSourceAdapter()]),
                new FakeEventService(), new FakeSourceRepository(null));
            var result = await sut.IngestAsync(SourceType.HTTP_API, Guid.NewGuid().ToString("D"),
                new object(), CancellationToken.None);
            Assert.Equal("not_found", result.Error?.Code);
        }

        private sealed class FakeSourceRepository : ISourceRepository
        {
            private readonly Source? _source;
            public FakeSourceRepository(Source? source) => _source = source;
            public Task<Result<Source?>> GetByIdAsync(string id, CancellationToken ct) =>
                Task.FromResult(Result.Success<Source?>(_source?.Id.Value == id ? _source : null));
            public Task<Result> InsertAsync(Source source, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result> UpdateAsync(Source source, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result> DeleteAsync(string id, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result<PagedResult<Source>>> ListAsync(int page, int size, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result<IReadOnlyList<Source>>> ListEnabledAsync(CancellationToken ct) =>
                Task.FromResult(Result.Success<IReadOnlyList<Source>>(_source is { Enabled: true } ? [_source] : []));
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
