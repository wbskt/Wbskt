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
        _databaseReader = databaseReader ?? throw new ArgumentNullException(nameof(databaseReader));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
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
        // This is not ideal, but for now we will rely on the user-specific cache.
        // In the future, we can implement a more granular cache for individual policies.
        var policies = await _cacheService.GetOrSetAsync("AllPolicies", () => _databaseReader.GetAllAsync(DateTime.UnixEpoch, cancellationToken), Constants.ExpiryTimes.CacheExpiry, cancellationToken);
        return policies.FirstOrDefault(p => p.RefId == refId);
    }

    public async Task<RegistrationPolicyRecord?> GetByPinAsync(string pin, CancellationToken cancellationToken)
    {
        // This is not ideal, but for now we will rely on the user-specific cache.
        // In the future, we can implement a more granular cache for individual policies.
        var policies = await _cacheService.GetOrSetAsync("AllPolicies", () => _databaseReader.GetAllAsync(DateTime.UnixEpoch, cancellationToken), Constants.ExpiryTimes.CacheExpiry, cancellationToken);
        return policies.FirstOrDefault(p => p.Pin == pin);
    }
}
