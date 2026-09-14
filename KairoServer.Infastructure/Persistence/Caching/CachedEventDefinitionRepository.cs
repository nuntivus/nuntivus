using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Caching;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Events;

namespace KairoServer.Infastructure.Persistence.Caching
{
    public sealed class CachedEventDefinitionRepository : IEventDefinitionRepository
    {
        private readonly IEventDefinitionRepository _inner;
        private readonly ICacheStore _cache;
        private readonly TimeSpan _ttl;

        public CachedEventDefinitionRepository(
            IEventDefinitionRepository inner,
            ICacheStore cache,
            CacheOption options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _ttl = (options ?? throw new ArgumentNullException(nameof(options))).Ttl;
        }

        public Task<Result> InsertAsync(
            EventDefinition definition,
            IUnitOfWork uow,
            CancellationToken cancellationToken) =>
            _inner.InsertAsync(definition, uow, cancellationToken);

        public Task<Result<EventDefinition?>> GetByEventTypeAsync(
            string eventType,
            CancellationToken cancellationToken) =>
            _cache.GetOrCreateAsync(
                $"{CacheKeys.EventDefinitionsByType}:{eventType}",
                _ttl,
                ct => _inner.GetByEventTypeAsync(eventType, ct),
                cancellationToken);

        public Task<Result<PagedResult<EventDefinition>>> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            _cache.GetOrCreateAsync(
                $"{CacheKeys.EventDefinitionsList}:{page}:{pageSize}",
                _ttl,
                ct => _inner.ListAsync(page, pageSize, ct),
                cancellationToken);
    }
}