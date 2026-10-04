using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NuntivusServer.Common;
using NuntivusServer.Infastructure.Caching;

namespace NuntivusServer.UnitTests.Caching
{
    public class InMemoryCacheStoreTests
    {
        [Fact]
        public async Task GetOrCreateAsync_DoesNotCacheFailedResult()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cache = new InMemoryCacheStore(memoryCache);
            var calls = 0;

            Task<Result<string?>> Factory(CancellationToken cancellationToken)
            {
                calls++;
                return Task.FromResult(calls == 1
                    ? Result.Failure<string?>(Error.Unexpected("Temporary failure."))
                    : Result.Success<string?>("recovered"));
            }

            var first = await cache.GetOrCreateAsync("customer:1", TimeSpan.FromMinutes(1), Factory, CancellationToken.None);
            var second = await cache.GetOrCreateAsync("customer:1", TimeSpan.FromMinutes(1), Factory, CancellationToken.None);
            var third = await cache.GetOrCreateAsync("customer:1", TimeSpan.FromMinutes(1), Factory, CancellationToken.None);

            Assert.True(first.IsFailure);
            Assert.True(second.IsSuccess);
            Assert.Equal("recovered", second.Value);
            Assert.Same(second, third);
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task GetOrCreateAsync_CachesSuccessfulResultWithNullValue()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cache = new InMemoryCacheStore(memoryCache);
            var calls = 0;

            Task<Result<string?>> Factory(CancellationToken cancellationToken)
            {
                calls++;
                return Task.FromResult(Result.Success<string?>(null));
            }

            var first = await cache.GetOrCreateAsync("customer:missing", TimeSpan.FromMinutes(1), Factory, CancellationToken.None);
            var second = await cache.GetOrCreateAsync("customer:missing", TimeSpan.FromMinutes(1), Factory, CancellationToken.None);

            Assert.True(first.IsSuccess);
            Assert.Null(first.Value);
            Assert.Same(first, second);
            Assert.Equal(1, calls);
        }
    }
}
