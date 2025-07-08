using System.Collections.Concurrent;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedEnrollmentPoliciesReader(ILogger<CachedEnrollmentPoliciesReader> logger, IEnrollmentPoliciesDatabaseReader enrollmentPoliciesReader)
    : IDatabaseChangeListener, IEnrollmentPoliciesReader
{
    private static DateTime _lastModified = DateTime.UnixEpoch;
    private static readonly ConcurrentDictionary<Guid, EnrollmentPolicyReadRecord> PoliciesByPolicyRefCache = [];
    private static readonly ConcurrentDictionary<int, EnrollmentPolicyReadRecord> PoliciesCache = [];

    public void RegisterDatabaseListener()
    {
        if (enrollmentPoliciesReader is EnrollmentPoliciesDatabaseReader enrollmentPoliciesReaderImp)
        {
            enrollmentPoliciesReaderImp.RegisterSqlDependency(OnDatabaseChange);
        }
    }

    public IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAll()
    {
        RefreshCacheIfEmpty();
        return [.. PoliciesCache.Values];
    }

    public IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAllByUserId(int userId)
    {
        RefreshCacheIfEmpty();
        return [.. PoliciesCache.Values.Where(p => p.UserId == userId)];
    }

    public EnrollmentPolicyReadRecord? GetById(int policyId)
    {
        RefreshCacheIfEmpty();
        return PoliciesCache.GetValueOrDefault(policyId);
    }

    public EnrollmentPolicyReadRecord? GetByRef(Guid policyRef)
    {
        RefreshCacheIfEmpty();
        return PoliciesByPolicyRefCache.GetValueOrDefault(policyRef);
    }

    private void OnDatabaseChange(object sender, SqlNotificationEventArgs e)
    {
        logger.LogInformation("database change detected: {Info}", e.Info);

        RefreshCache();
        RegisterDatabaseListener(); // re-register after change
    }

    private void RefreshCacheIfEmpty()
    {
        if (PoliciesCache.IsEmpty || PoliciesByPolicyRefCache.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        try
        {
            var policies = enrollmentPoliciesReader.GetAll(_lastModified);
            var maxLastModified = _lastModified;

            foreach (var policy in policies)
            {
                PoliciesCache.AddOrUpdate(policy.Id, policy, (_, _) => policy);
                PoliciesByPolicyRefCache.AddOrUpdate(policy.PolicyRef, policy, (_, _) => policy);

                if (policy.LastModified > maxLastModified)
                {
                    maxLastModified = policy.LastModified;
                }
            }

            _lastModified = maxLastModified;
            logger.LogDebug("cache refreshed with {count} policies", policies.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to refresh enrollment policies cache");
            throw;
        }
    }
}
