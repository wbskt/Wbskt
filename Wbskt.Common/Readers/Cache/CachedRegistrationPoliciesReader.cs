using Microsoft.Extensions.Caching.Memory;
using Wbskt.Common.Records;
using Wbskt.Common.Readers.Database;

namespace Wbskt.Common.Readers.Cache;

/// <summary>
/// A cached implementation of the registration policy reader to reduce database load.
/// </summary>
internal sealed class CachedRegistrationPoliciesReader : IRegistrationPoliciesReader
{
    private readonly IRegistrationPoliciesDatabaseReader _databaseReader;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public CachedRegistrationPoliciesReader(
        IRegistrationPoliciesDatabaseReader databaseReader,
        IMemoryCache cache)
    {
        _databaseReader = databaseReader;
        _cache = cache;
    }

    public async Task<List<RegistrationPolicyRecord>> GetAllAsync(int userId, CancellationToken cancellationToken)
    {
        var cacheKey = $"RegistrationPolicies_User_{userId}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _databaseReader.GetAllAsync(userId, cancellationToken);
        });
    }
}
