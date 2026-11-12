using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Events;
using NuntivusServer.Application.Events.Queries;
using NuntivusServer.Application.Auditing;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Common.Logging;
using NuntivusServer.Domain.Events;
using NuntivusServer.Domain.Outbox;
using NuntivusServer.Infastructure.Events;

namespace NuntivusServer.UnitTests.Events
{
    public sealed class EventServiceTests
    {
        [Fact]
        public async Task Replay_missing_source_event_returns_retention_limitation_message()
        {
            var service = new EventService(
                new FakeEventRepository(),
                new FakeOutboxRepository(),
                new FakeAuditLogRepository(),
                new FakeUnitOfWork(),
                new NullLogger<EventService>(),
                new FakeEventDefinitionRepository(),
                new JsonEventSchemaValidator());

            var eventId = Guid.NewGuid();
            var result = await service.ReplayAsync(eventId, "manual-1", null, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("not_found", result.Error?.Code);
            Assert.Contains("only available while source events are retained", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Replay_event_insert_race_returns_committed_winner_without_duplicate_writes()
        {
            var source = Event.Create("source", "orders.created", "source", DateTimeOffset.UtcNow, "{}", (EventMetadata?)null);
            var winningId = Guid.NewGuid();
            var events = new FakeEventRepository
            {
                Source = source,
                InsertConflict = true,
                WinningReplay = new EventReplay(source.Id, "same-key", winningId, DateTimeOffset.UtcNow),
            };
            var outbox = new FakeOutboxRepository();
            var uow = new FakeUnitOfWork();
            var service = CreateService(events, new FakeEventDefinitionRepository(), outbox, uow);

            var result = await service.ReplayAsync(source.Id, "same-key", null, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.Created);
            Assert.Equal(winningId, result.Value.ReplayedEventId);
            Assert.Equal(0, outbox.Count);
            Assert.Equal(0, events.ReplayWrites);
            Assert.Equal(1, uow.RollbackCount);
        }

        [Fact]
        public async Task Publish_without_schema_records_not_configured()
        {
            var events = new FakeEventRepository();
            var service = CreateService(events, new FakeEventDefinitionRepository(), new FakeOutboxRepository(), new FakeUnitOfWork());

            var result = await service.PublishAsync("new", "orders.created", "source", "{}", DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(EventSchemaValidationStatus.NotConfigured, events.Inserted!.SchemaValidationStatus);
            Assert.Null(events.Inserted.InputSchemaVersion);
        }

        [Theory]
        [InlineData("{\"nuntivus.test\":\"true\"}")]
        [InlineData("{\"NUNTIVUS.replay.sourceEventId\":\"original\"}")]
        public async Task Publishers_cannot_forge_internal_measurement_exclusions(string metadata)
        {
            var events = new FakeEventRepository();
            var uow = new FakeUnitOfWork();
            var service = CreateService(events, new FakeEventDefinitionRepository(), new FakeOutboxRepository(), uow);
            var result = await service.PublishAsync("new", "orders.created", "source", "{}", DateTimeOffset.UtcNow, metadata, CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal("validation", result.Error?.Code);
            Assert.Null(events.Inserted);
            Assert.Equal(0, uow.BeginCount);
        }

        [Fact]
        public async Task Publish_valid_payload_records_selected_schema_version()
        {
            var events = new FakeEventRepository();
            var definitions = new FakeEventDefinitionRepository { Active = CreateSchema() };
            var outbox = new FakeOutboxRepository();
            var service = CreateService(events, definitions, outbox, new FakeUnitOfWork());

            var result = await service.PublishAsync("new", "orders.created", "source", "{\"orderId\":\"42\"}", DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(EventSchemaValidationStatus.Valid, events.Inserted!.SchemaValidationStatus);
            Assert.Equal(1, events.Inserted.InputSchemaVersion);
            Assert.Equal(1, outbox.Count);
        }

        [Fact]
        public async Task Publish_invalid_payload_does_not_open_transaction_or_write_event_or_outbox()
        {
            var events = new FakeEventRepository();
            var definitions = new FakeEventDefinitionRepository { Active = CreateSchema() };
            var outbox = new FakeOutboxRepository();
            var uow = new FakeUnitOfWork();
            var service = CreateService(events, definitions, outbox, uow);

            var result = await service.PublishAsync("new", "orders.created", "source", "{\"orderId\":42}", DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains(result.Errors, error => error.Path == "/payload/orderId" && error.Keyword == "type");
            Assert.Null(events.Inserted);
            Assert.Equal(0, outbox.Count);
            Assert.Equal(0, uow.BeginCount);
        }

        [Fact]
        public async Task Duplicate_returns_original_without_looking_up_current_schema()
        {
            var original = Event.Create("duplicate", "orders.created", "source", DateTimeOffset.UtcNow, "{}",
                (EventMetadata?)null, null, EventSchemaValidationStatus.NotConfigured);
            var events = new FakeEventRepository { Existing = original };
            var definitions = new FakeEventDefinitionRepository { Active = CreateSchema() };
            var service = CreateService(events, definitions, new FakeOutboxRepository(), new FakeUnitOfWork());

            var result = await service.PublishAsync("duplicate", "orders.created", "source", "{}", DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(original.Id, result.Value);
            Assert.Equal(0, definitions.ActiveReads);
            Assert.Null(events.Inserted);
        }

        private static EventService CreateService(FakeEventRepository events, FakeEventDefinitionRepository definitions,
            FakeOutboxRepository outbox, FakeUnitOfWork uow) =>
            new(events, outbox, new FakeAuditLogRepository(), uow, new NullLogger<EventService>(), definitions, new JsonEventSchemaValidator());

        private static EventSchemaVersion CreateSchema() => EventSchemaVersion.CreateDraft("orders.created", 1,
            """{"type":"object","required":["orderId"],"properties":{"orderId":{"type":"string"}}}""",
            SchemaCompatibilityMode.Backward, DateTimeOffset.UtcNow);

        private sealed class FakeEventRepository : IEventRepository
        {
            public Event? Inserted { get; private set; }
            public Event? Existing { get; init; }
            public Event? Source { get; init; }
            public EventReplay? WinningReplay { get; init; }
            public bool InsertConflict { get; init; }
            public int ReplayWrites { get; private set; }
            private int _replayReads;
            public Task<Result> InsertAsync(Event @event, IUnitOfWork uow, CancellationToken cancellationToken)
            {
                Inserted = @event;
                return Task.FromResult(InsertConflict ? Result.Failure(Error.Conflict("Concurrent insert.")) : Result.Success());
            }

            public Task<Result<Event?>> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(Source));

            public Task<Result<EventQueryView?>> GetQueryByIdAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<EventQueryView?>(null));

            public Task<Result<PagedResult<EventQueryView>>> ListQueryAsync(EventListQuery query, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(PagedResult<EventQueryView>.Empty(query.Page, query.PageSize)));

            public Task<Result<Event?>> GetByIngestionKeyAsync(string sourceId, string idempotencyKey, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(Existing));

            public Task<Result<EventReplay?>> GetReplayAsync(Guid sourceEventId, string replayKey, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(++_replayReads > 1 ? WinningReplay : null));

            public Task<Result> InsertReplayAsync(EventReplay replay, IUnitOfWork uow, CancellationToken cancellationToken)
            {
                ReplayWrites++;
                return Task.FromResult(Result.Success());
            }
        }

        private sealed class FakeOutboxRepository : IOutboxRepository
        {
            public int Count { get; private set; }
            public Task<Result> InsertAsync(OutboxMessage message, IUnitOfWork uow, CancellationToken cancellationToken)
            {
                Count++;
                return Task.FromResult(Result.Success());
            }
        }

        private sealed class FakeAuditLogRepository : IAuditLogRepository
        {
            public Task<Result<int>> AddAsync(NuntivusServer.Domain.Auditing.AuditLogEntry entry, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(1));

            public Task<Result<PagedResult<AuditLogListItemView>>> ListAsync(
                AuditLogListQuery query,
                CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(PagedResult<AuditLogListItemView>.Empty(query.Page, query.PageSize)));
        }

        private sealed class FakeUnitOfWork : IUnitOfWork
        {
            public int BeginCount { get; private set; }
            public int RollbackCount { get; private set; }
            public System.Data.IDbConnection? GetConnection() => null;
            public System.Data.IDbTransaction? GetTransaction() => null;
            public void Begin(bool inTransaction = true) { BeginCount++; }
            public void Commit() { }
            public void Rollback() { RollbackCount++; }
        }

        private sealed class FakeEventDefinitionRepository : IEventDefinitionRepository
        {
            public Task<Result> LockDefinitionAsync(string eventType, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
            public EventSchemaVersion? Active { get; init; }
            public int ActiveReads { get; private set; }
            public Task<Result<EventSchemaVersion?>> GetActiveSchemaVersionAsync(string eventType, CancellationToken cancellationToken)
            {
                ActiveReads++;
                return Task.FromResult(Result.Success(Active));
            }
            public Task<Result> InsertAsync(EventDefinition definition, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result<EventDefinition?>> GetByEventTypeAsync(string eventType, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result<PagedResult<EventDefinition>>> ListAsync(int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result<int>> UpdateAsync(string eventType, string displayName, string? description, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result> InsertSchemaVersionAsync(EventSchemaVersion schema, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result<EventSchemaVersion?>> GetSchemaVersionAsync(string eventType, int version, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result<PagedResult<EventSchemaVersion>>> ListSchemaVersionsAsync(string eventType, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result> ActivateSchemaVersionAsync(string eventType, int version, DateTimeOffset at, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
            public Task<Result> DeprecateSchemaVersionAsync(string eventType, int version, DateTimeOffset at, IUnitOfWork uow, CancellationToken ct) => throw new NotImplementedException();
        }

        private sealed class NullLogger<T> : IAppLogger<T>
        {
            public void Debug(string messageTemplate, params object?[] args) { }
            public void Information(string messageTemplate, params object?[] args) { }
            public void Warning(string messageTemplate, params object?[] args) { }
            public void Error(string messageTemplate, params object?[] args) { }
            public void Error(Exception exception, string messageTemplate, params object?[] args) { }
            public void Fatal(string messageTemplate, params object?[] args) { }
            public void Fatal(Exception exception, string messageTemplate, params object?[] args) { }
        }
    }
}
