using Microsoft.Extensions.Caching.Memory;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedUsersReader : IUsersReader
{
    private readonly IUsersDatabaseReader _usersDatabaseReader;
    private readonly IMemoryCache _memoryCache;

    public CachedUsersReader(IUsersDatabaseReader usersDatabaseReader, IMemoryCache memoryCache)
    {
        _usersDatabaseReader = usersDatabaseReader;
        _memoryCache = memoryCache;
    }

    public async Task<UserRecord?> GetByIdAsync(int userId, CancellationToken cancellationToken)
    {
        var cacheKey = $"User_{userId}";

        if (_memoryCache.TryGetValue(cacheKey, out UserRecord? user))
        {
            return user;
        }

        user = await _usersDatabaseReader.GetByIdAsync(userId, cancellationToken);

        if (user != null)
        {
            _memoryCache.Set(cacheKey, user, TimeSpan.FromMinutes(5));
        }

        return user;
    }

    public async Task<UserRecord?> GetByEmailIdAsync(string emailId, CancellationToken cancellationToken)
    {
        var cacheKey = $"User_{emailId}";

        if (_memoryCache.TryGetValue(cacheKey, out UserRecord? user))
        {
            return user;
        }

        user = await _usersDatabaseReader.GetByEmailIdAsync(emailId, cancellationToken);

        if (user != null)
        {
            _memoryCache.Set(cacheKey, user, TimeSpan.FromMinutes(5));
        }

        return user;
    }

    public Task<int> FindByEmailIdAsync(string emailId, CancellationToken cancellationToken)
    {
        // This method is not cached as it's a simple lookup and the result might change frequently.
        return _usersDatabaseReader.FindByEmailIdAsync(emailId, cancellationToken);
    }
}
