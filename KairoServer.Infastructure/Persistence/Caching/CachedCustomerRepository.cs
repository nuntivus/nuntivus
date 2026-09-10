using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Caching;
using KairoServer.Application.Persistence;
using KairoServer.Common;
using KairoServer.Domain.Customers;

namespace KairoServer.Infastructure.Persistence.Caching
{
    /// <summary>
    /// TTL-caching decorator for <see cref="ICustomerRepository"/>. Writes pass
    /// through; reads within the TTL are served from cache.
    /// </summary>
    public sealed class CachedCustomerRepository : ICustomerRepository
    {
        private readonly ICustomerRepository _inner;
        private readonly ICacheStore _cache;
        private readonly TimeSpan _ttl;

        public CachedCustomerRepository(ICustomerRepository inner, ICacheStore cache, CacheOption options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _ttl = (options ?? throw new ArgumentNullException(nameof(options))).Ttl;
        }

        public Task<Result> InsertAsync(Customer customer, IUnitOfWork uow, CancellationToken cancellationToken) =>
            _inner.InsertAsync(customer, uow, cancellationToken);

        public Task<Result> UpdateAsync(Customer customer, IUnitOfWork uow, CancellationToken cancellationToken) =>
            _inner.UpdateAsync(customer, uow, cancellationToken);

        public Task<Result<Customer?>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            _cache.GetOrCreateAsync(
                $"{CacheKeys.CustomersById}:{id}",
                _ttl,
                ct => _inner.GetByIdAsync(id, ct),
                cancellationToken);

        public Task<Result<PagedResult<Customer>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            _inner.ListAsync(page, pageSize, cancellationToken);
    }
}
