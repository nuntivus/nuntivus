using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Caching;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Events;

namespace NuntivusServer.Infastructure.Persistence.Caching
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

        public Task<Result> LockDefinitionAsync(string eventType, IUnitOfWork uow, CancellationToken cancellationToken) =>
            _inner.LockDefinitionAsync(eventType, uow, cancellationToken);

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

        public async Task<Result<int>> UpdateAsync(
            string eventType,
            string displayName,
            string? description,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            var result = await _inner.UpdateAsync(eventType, displayName, description, uow, cancellationToken);
            if (result.IsSuccess && result.Value > 0)
                _cache.Remove($"{CacheKeys.EventDefinitionsByType}:{eventType}");

            return result;
        }

        public Task<Result> InsertSchemaVersionAsync(
            EventSchemaVersion schemaVersion,
            IUnitOfWork uow,
            CancellationToken cancellationToken) =>
            _inner.InsertSchemaVersionAsync(schemaVersion, uow, cancellationToken);

        public Task<Result<EventSchemaVersion?>> GetSchemaVersionAsync(
            string eventType,
            int version,
            CancellationToken cancellationToken) =>
            _inner.GetSchemaVersionAsync(eventType, version, cancellationToken);

        public Task<Result<PagedResult<EventSchemaVersion>>> ListSchemaVersionsAsync(
            string eventType,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            _inner.ListSchemaVersionsAsync(eventType, page, pageSize, cancellationToken);

        public Task<Result<EventSchemaVersion?>> GetActiveSchemaVersionAsync(
            string eventType,
            CancellationToken cancellationToken) =>
            _inner.GetActiveSchemaVersionAsync(eventType, cancellationToken);

        public Task<Result> ActivateSchemaVersionAsync(
            string eventType,
            int version,
            DateTimeOffset activatedAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken) =>
            _inner.ActivateSchemaVersionAsync(eventType, version, activatedAt, uow, cancellationToken);

        public Task<Result> DeprecateSchemaVersionAsync(
            string eventType,
            int version,
            DateTimeOffset deprecatedAt,
            IUnitOfWork uow,
            CancellationToken cancellationToken) =>
            _inner.DeprecateSchemaVersionAsync(eventType, version, deprecatedAt, uow, cancellationToken);
    }
}