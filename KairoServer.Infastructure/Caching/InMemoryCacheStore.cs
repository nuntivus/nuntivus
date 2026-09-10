using System;
using System.Threading;
using System.Threading.Tasks;
using KairoServer.Application.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace KairoServer.Infastructure.Caching
{
    /// <summary>
    /// <see cref="ICacheStore"/> implementation backed by <see cref="IMemoryCache"/>.
    /// Applies an absolute TTL per entry. Single-flight is not enforced, so on a
    /// cold key multiple concurrent callers may all invoke the factory; the last
    /// writer wins. This is acceptable for the matcher hot path.
    /// </summary>
    public sealed class InMemoryCacheStore : ICacheStore
    {
        private readonly IMemoryCache _cache;

        public InMemoryCacheStore(IMemoryCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            TimeSpan ttl,
            Func<CancellationToken, Task<T>> factory,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Cache key cannot be null or whitespace.", nameof(key));
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            {
                return cached;
            }

            var value = await factory(cancellationToken).ConfigureAwait(false);

            _cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
            });

            return value;
        }

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _cache.Remove(key);
        }
    }
}
