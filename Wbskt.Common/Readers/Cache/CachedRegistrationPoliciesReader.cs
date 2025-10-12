using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

/// <summary>
/// A cached implementation of the registration policy reader that uses a ConcurrentDictionary and SqlDependency.
/// </summary>
internal sealed class CachedRegistrationPoliciesReader : IRegistrationPoliciesReader
{
    private readonly ILogger<CachedRegistrationPoliciesReader> _logger;
    private readonly IRegistrationPoliciesDatabaseReader _databaseReader;

    private static DateTime _lastModified = DateTime.UnixEpoch;
    private static readonly ConcurrentDictionary<int, RegistrationPolicyRecord> PoliciesByIdCache = [];

    public CachedRegistrationPoliciesReader(
        ILogger<CachedRegistrationPoliciesReader> logger,
        IRegistrationPoliciesDatabaseReader databaseReader)
    {
        _logger = logger;
        _databaseReader = databaseReader;
    }

    public async Task<List<RegistrationPolicyRecord>> GetAllAsync(int userId, CancellationToken cancellationToken)
    {
        await RefreshCacheIfEmpty(cancellationToken);
        return PoliciesByIdCache.Values.Where(p => p.UserId == userId).ToList();
    }

    private async Task RefreshCacheIfEmpty(CancellationToken cancellationToken)
    {
        if (PoliciesByIdCache.IsEmpty)
        {
            await RefreshCache(cancellationToken);
        }
    }

    private async Task RefreshCache(CancellationToken cancellationToken)
    {
        try
        {
            var policies = await _databaseReader.GetAllAsync(_lastModified, cancellationToken);
            var maxLastModified = _lastModified;

            foreach (var policy in policies)
            {
                PoliciesByIdCache[policy.Id] = policy;

                if (policy.LastModified > maxLastModified)
                {
                    maxLastModified = policy.LastModified;
                }
            }

            _lastModified = maxLastModified;
            _logger.LogDebug("RegistrationPolicies cache refreshed with {count} policies", policies.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh RegistrationPolicies cache");
            throw;
        }
    }
}
