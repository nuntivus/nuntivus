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
                new NullLogger<EventService>());

            var eventId = Guid.NewGuid();
            var result = await service.ReplayAsync(eventId, "manual-1", null, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("not_found", result.Error?.Code);
            Assert.Contains("only available while source events are retained", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class FakeEventRepository : IEventRepository
        {
            public Task<Result> InsertAsync(Event @event, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success());

            public Task<Result<Event?>> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<Event?>(null));

            public Task<Result<EventQueryView?>> GetQueryByIdAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<EventQueryView?>(null));

            public Task<Result<PagedResult<EventQueryView>>> ListQueryAsync(EventListQuery query, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success(PagedResult<EventQueryView>.Empty(query.Page, query.PageSize)));

            public Task<Result<Event?>> GetByIngestionKeyAsync(string sourceId, string idempotencyKey, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<Event?>(null));

            public Task<Result<EventReplay?>> GetReplayAsync(Guid sourceEventId, string replayKey, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success<EventReplay?>(null));

            public Task<Result> InsertReplayAsync(EventReplay replay, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success());
        }

        private sealed class FakeOutboxRepository : IOutboxRepository
        {
            public Task<Result> InsertAsync(OutboxMessage message, IUnitOfWork uow, CancellationToken cancellationToken) =>
                Task.FromResult(Result.Success());
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
            public System.Data.IDbConnection? GetConnection() => null;
            public System.Data.IDbTransaction? GetTransaction() => null;
            public void Begin(bool inTransaction = true) { }
            public void Commit() { }
            public void Rollback() { }
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
