using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.Common.Services;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedRegistrationPoliciesReader : IRegistrationPoliciesReader
{
    private readonly IRegistrationPoliciesDatabaseReader _databaseReader;
    private readonly ICacheService _cacheService;

    public CachedRegistrationPoliciesReader(IRegistrationPoliciesDatabaseReader databaseReader, ICacheService cacheService)
    {
        _databaseReader = databaseReader;
        _cacheService = cacheService;
    }

    public Task<List<RegistrationPolicyRecord>> GetAllAsync(int userId, CancellationToken cancellationToken)
    {
        var cacheKey = $"Policies_User_{userId}";
        return _cacheService.GetOrSetAsync(cacheKey, async () =>
        {
            var policies = await _databaseReader.GetAllAsync(DateTime.UnixEpoch, cancellationToken);
            return policies.Where(p => p.UserId == userId).ToList();
        }, Constants.ExpiryTimes.CacheExpiry, cancellationToken);
    }

    public async Task<RegistrationPolicyRecord?> GetByRefIdAsync(int userId, Guid refId, CancellationToken cancellationToken)
    {
        var policies = await GetAllAsync(userId, cancellationToken);
        return policies.FirstOrDefault(p => p.RefId == refId);
    }
}
