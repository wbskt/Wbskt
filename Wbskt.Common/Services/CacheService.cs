using Microsoft.Extensions.Caching.Memory;

namespace Wbskt.Common.Services;

internal sealed class CacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;

    public CacheService(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public async Task<T> GetOrSetAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(cacheKey, out T value))
        {
            return value!;
        }

        var result = await factory();

        if (result != null)
        {
            _memoryCache.Set(cacheKey, result, duration);
        }

        return result;
    }
}
