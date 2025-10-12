namespace Wbskt.Common.Services;

public interface ICacheService
{
    Task<T> GetOrSetAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan duration, CancellationToken cancellationToken);
}
