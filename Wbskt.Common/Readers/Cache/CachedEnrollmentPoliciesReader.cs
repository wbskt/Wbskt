using System.Collections.Concurrent;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedEnrollmentPoliciesReader(ILogger<CachedEnrollmentPoliciesReader> logger, IEnrollmentPoliciesDatabaseReader enrollmentPoliciesReader) : IDatabaseChangeListener, IEnrollmentPoliciesReader
{
    private static DateTime _lastModified = DateTime.MinValue;
    private readonly ConcurrentDictionary<int, EnrollmentPolicyReadRecord> policiesCache = [];
    private readonly ConcurrentDictionary<Guid, EnrollmentPolicyReadRecord> policiesByPolicyRefCache = [];

    public IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAll()
    {
        RefreshCacheIfEmpty();
        return [.. policiesCache.Values];
    }

    public IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAllByUserId(int userId)
    {
        RefreshCacheIfEmpty();
        return [.. policiesCache.Values.Where(p => p.UserId == userId)];
    }

    public EnrollmentPolicyReadRecord? GetByCode(string code)
    {
        RefreshCacheIfEmpty();
        if (Guid.TryParse(code, out var policyRef) && policiesByPolicyRefCache.TryGetValue(policyRef, out var record))
        {
            return record;
        }

        return null;
    }

    public void RegisterDatabaseListener()
    {
        if (enrollmentPoliciesReader is EnrollmentPoliciesDatabaseReader enrollmentPoliciesReaderImp)
        {
            enrollmentPoliciesReaderImp.RegisterSqlDependency(OnDatabaseChange);
        }
    }

    private void OnDatabaseChange(object sender, SqlNotificationEventArgs e)
    {
        logger.LogInformation("database change detected: {Info}", e.Info);

        RefreshCache();
        RegisterDatabaseListener(); // re-register after change
    }

    private void RefreshCacheIfEmpty()
    {
        if (policiesCache.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        var latestPolicies = enrollmentPoliciesReader.GetAll(_lastModified);
        foreach (var record in latestPolicies)
        {
            policiesCache[record.Id] = record;
            policiesByPolicyRefCache[record.PolicyRef] = record;

            if (record.LastModified > _lastModified)
            {
                _lastModified = record.LastModified;
            }
        }
    }
}
