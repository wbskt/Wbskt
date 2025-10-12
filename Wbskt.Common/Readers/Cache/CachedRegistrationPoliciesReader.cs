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

    public async Task<List<RegistrationPolicyRecord>> GetAllAsync(int userId, CancellationToken cancellationToken)
    {
        var policies = await GetAllPoliciesAsync(cancellationToken);
        return policies.Where(p => p.UserId == userId).ToList();
    }

    private Task<List<RegistrationPolicyRecord>> GetAllPoliciesAsync(CancellationToken cancellationToken)
    {
        return _cacheService.GetOrSetAsync("AllPolicies", () => _databaseReader.GetAllAsync(DateTime.UnixEpoch, cancellationToken), Constants.ExpiryTimes.CacheExpiry, cancellationToken);
    }
}
