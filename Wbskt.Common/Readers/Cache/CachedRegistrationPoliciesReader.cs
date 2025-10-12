using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

/// <summary>
/// A cached implementation of the registration policy reader that uses a ConcurrentDictionary and SqlDependency.
/// </summary>
public class CachedRegistrationPoliciesReader : IRegistrationPoliciesReader, IDatabaseChangeListener
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

    public void RegisterDatabaseListener()
    {
        if (_databaseReader is Database.Implementation.RegistrationPoliciesDatabaseReader dbReaderImpl)
        {
            dbReaderImpl.RegisterSqlDependency(OnDatabaseChange);
        }
    }

    private void OnDatabaseChange(object sender, SqlNotificationEventArgs e)
    {
        _logger.LogInformation("Database change detected for RegistrationPolicies: {Info}", e.Info);
        RegisterDatabaseListener();
        _ = RefreshCache(CancellationToken.None);
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
