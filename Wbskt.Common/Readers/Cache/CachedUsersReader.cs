using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.Common.Services;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedUsersReader : IUsersReader
{
    private readonly IUsersDatabaseReader _usersDatabaseReader;
    private readonly ICacheService _cacheService;

    public CachedUsersReader(IUsersDatabaseReader usersDatabaseReader, ICacheService cacheService)
    {
        _usersDatabaseReader = usersDatabaseReader ?? throw new ArgumentNullException(nameof(usersDatabaseReader));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public Task<UserRecord?> GetByIdAsync(int userId, CancellationToken cancellationToken)
    {
        var cacheKey = $"User_{userId}";
        return _cacheService.GetOrSetAsync(cacheKey, () => _usersDatabaseReader.GetByIdAsync(userId, cancellationToken), Constants.ExpiryTimes.CacheExpiry, cancellationToken);
    }

    public Task<UserRecord?> GetByEmailIdAsync(string emailId, CancellationToken cancellationToken)
    {
        var cacheKey = $"User_{emailId}";
        return _cacheService.GetOrSetAsync(cacheKey, () => _usersDatabaseReader.GetByEmailIdAsync(emailId, cancellationToken), Constants.ExpiryTimes.CacheExpiry, cancellationToken);
    }

    public Task<int> FindByEmailIdAsync(string emailId, CancellationToken cancellationToken)
    {
        // This method is not cached as it's a simple lookup and the result might change frequently.
        return _usersDatabaseReader.FindByEmailIdAsync(emailId, cancellationToken);
    }
}
