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
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
        if (_memoryCache.TryGetValue(cacheKey, out T value))
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
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
