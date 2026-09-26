using System;
using System.Threading;
using System.Threading.Tasks;

namespace NuntivusServer.Application.Caching
{
    /// <summary>
    /// Minimal cache abstraction used by repository decorators.
    /// Backed by in-memory <c>IMemoryCache</c> today; a Redis-backed
    /// implementation can be plugged in without touching the decorators.
    /// </summary>
    public interface ICacheStore
    {
        /// <summary>
        /// Returns a cached value or invokes <paramref name="factory"/> to compute and store it
        /// with the given absolute <paramref name="ttl"/>.
        /// </summary>
        Task<T> GetOrCreateAsync<T>(
            string key,
            TimeSpan ttl,
            Func<CancellationToken, Task<T>> factory,
            CancellationToken cancellationToken);

        void Remove(string key);
    }
}
