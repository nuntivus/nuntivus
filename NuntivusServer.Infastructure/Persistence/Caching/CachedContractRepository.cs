using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Application.Caching;
using NuntivusServer.Application.Contracts;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;

namespace NuntivusServer.Infastructure.Persistence.Caching
{
    /// <summary>
    /// TTL-caching decorator for <see cref="IContractRepository"/>.
    /// Reads are cached under stable prefixes; writes pass through and rely on
    /// the TTL for eventual consistency (max staleness == <see cref="CacheOption.Ttl"/>).
    /// </summary>
    public sealed class CachedContractRepository : IContractRepository
    {
        private readonly IContractRepository _inner;
        private readonly ICacheStore _cache;
        private readonly TimeSpan _ttl;

        public CachedContractRepository(IContractRepository inner, ICacheStore cache, CacheOption options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _ttl = (options ?? throw new ArgumentNullException(nameof(options))).Ttl;
        }

        public Task<Result> InsertAsync(Contract contract, IUnitOfWork uow, CancellationToken cancellationToken) =>
            _inner.InsertAsync(contract, uow, cancellationToken);

        public Task<Result> UpdateAsync(Contract contract, IUnitOfWork uow, CancellationToken cancellationToken) =>
            _inner.UpdateAsync(contract, uow, cancellationToken);

        public Task<Result<Contract?>> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            _cache.GetOrCreateAsync(
                $"{CacheKeys.ContractsById}:{id}",
                _ttl,
                ct => _inner.GetByIdAsync(id, ct),
                cancellationToken);

        public Task<Result<PagedResult<Contract>>> ListByCustomerAsync(
            ContractListQuery query,
            CancellationToken cancellationToken) =>
            _cache.GetOrCreateAsync(
                $"{CacheKeys.ContractsByCustomer}:{query.CustomerId}:{query.Enabled}:{query.EventType}:{query.CreatedFrom:O}:{query.CreatedTo:O}:{query.SortBy}:{query.SortDirection}:{query.Page}:{query.PageSize}",
                _ttl,
                ct => _inner.ListByCustomerAsync(query, ct),
                cancellationToken);

        public Task<Result<PagedResult<Contract>>> ListAsync(
            AdminContractListQuery query,
            CancellationToken cancellationToken) =>
            _cache.GetOrCreateAsync(
                $"{CacheKeys.ContractsByCustomer}:admin:{query.CustomerId}:{query.Enabled}:{query.DestinationType}:{query.EventType}:{query.Search}:{query.CreatedFrom:O}:{query.CreatedTo:O}:{query.SortBy}:{query.SortDirection}:{query.Page}:{query.PageSize}",
                _ttl,
                ct => _inner.ListAsync(query, ct),
                cancellationToken);

        public Task<Result<IReadOnlyList<ContractMatch>>> ListEnabledMatchingAsync(
            string eventType, string sourceId, CancellationToken cancellationToken) =>
            _cache.GetOrCreateAsync(
                $"{CacheKeys.ContractsMatch}:{eventType}:{sourceId}",
                _ttl,
                ct => _inner.ListEnabledMatchingAsync(eventType, sourceId, ct),
                cancellationToken);
    }
}
