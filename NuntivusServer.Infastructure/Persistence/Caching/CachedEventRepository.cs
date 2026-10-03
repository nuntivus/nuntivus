using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Caching;
using NuntivusServer.Application.Events.Queries;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Events;

namespace NuntivusServer.Infastructure.Persistence.Caching
{
    /// <summary>
    /// TTL-caching decorator for <see cref="IEventRepository"/>.
    /// Events are immutable once written, so caching <c>GetByIdAsync</c> is
    /// safe for the full <see cref="CacheOption.Ttl"/> window.
    /// </summary>
    public sealed class CachedEventRepository : IEventRepository
    {
        private readonly IEventRepository _inner;
        private readonly ICacheStore _cache;
        private readonly TimeSpan _ttl;

        public CachedEventRepository(IEventRepository inner, ICacheStore cache, CacheOption options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _ttl = (options ?? throw new ArgumentNullException(nameof(options))).Ttl;
        }

        public Task<Result> InsertAsync(Event @event, IUnitOfWork uow, CancellationToken cancellationToken) =>
            _inner.InsertAsync(@event, uow, cancellationToken);

        public Task<Result<Event?>> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            _cache.GetOrCreateAsync(
                $"{CacheKeys.EventsById}:{id}",
                _ttl,
                ct => _inner.GetByIdAsync(id, ct),
                cancellationToken);

        public Task<Result<EventQueryView?>> GetQueryByIdAsync(Guid id, CancellationToken cancellationToken) =>
            _inner.GetQueryByIdAsync(id, cancellationToken);

        public Task<Result<PagedResult<EventQueryView>>> ListQueryAsync(
            EventListQuery query,
            CancellationToken cancellationToken) =>
            _inner.ListQueryAsync(query, cancellationToken);

        public Task<Result<Event?>> GetByIngestionKeyAsync(
            string sourceId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            _inner.GetByIngestionKeyAsync(sourceId, idempotencyKey, cancellationToken);

        public Task<Result<EventReplay?>> GetReplayAsync(
            Guid sourceEventId,
            string replayKey,
            CancellationToken cancellationToken) =>
            _inner.GetReplayAsync(sourceEventId, replayKey, cancellationToken);

        public Task<Result> InsertReplayAsync(
            EventReplay replay,
            IUnitOfWork uow,
            CancellationToken cancellationToken) =>
            _inner.InsertReplayAsync(replay, uow, cancellationToken);
    }
}
