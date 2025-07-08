using System.Collections.Concurrent;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedPublishersReader(ILogger<CachedPublishersReader> logger, IPublishersDatabaseReader publishersReader) : IDatabaseChangeListener, IPublishersReader
{
    private static DateTime _lastModified = DateTime.UnixEpoch;
    private static readonly ConcurrentDictionary<Guid, PublisherReadRecord> PublishersByGuidCache = [];
    private static readonly ConcurrentDictionary<int, PublisherReadRecord> PublishersCache = [];

    public void RegisterDatabaseListener()
    {
        if (publishersReader is PublishersDatabaseReader publishersReaderImp)
        {
            publishersReaderImp.RegisterSqlDependency(OnDatabaseChange);
        }
    }

    public PublisherReadRecord GetById(int id)
    {
        RefreshCacheIfEmpty();
        if (PublishersCache.TryGetValue(id, out var record))
        {
            return record;
        }

        throw WbsktExceptions.PublisherIdNotExists(id);
    }

    public PublisherReadRecord GetByRef(Guid publisherRef)
    {
        RefreshCacheIfEmpty();
        if (PublishersByGuidCache.TryGetValue(publisherRef, out var record))
        {
            return record;
        }

        throw WbsktExceptions.PublisherRefNotExists(publisherRef);
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAll()
    {
        RefreshCacheIfEmpty();
        return [.. PublishersCache.Values];
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAllByUserId(int userId)
    {
        RefreshCacheIfEmpty();
        return [.. PublishersCache.Values.Where(c => c.UserId == userId)];
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAllByIds(int[] ids)
    {
        RefreshCacheIfEmpty();
        var result = new List<PublisherReadRecord>(ids.Length);

        foreach (var id in ids)
        {
            if (PublishersCache.TryGetValue(id, out var record))
            {
                result.Add(record);
            }
        }

        return result.AsReadOnly();
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAllByRefs(Guid[] publisherRefs)
    {
        RefreshCacheIfEmpty();
        var result = new List<PublisherReadRecord>(publisherRefs.Length);

        foreach (var publisherRef in publisherRefs)
        {
            if (PublishersByGuidCache.TryGetValue(publisherRef, out var record))
            {
                result.Add(record);
            }
        }

        return result.AsReadOnly();
    }

    private void OnDatabaseChange(object sender, SqlNotificationEventArgs e)
    {
        logger.LogInformation("database change detected: {Info}", e.Info);

        RefreshCache();
        RegisterDatabaseListener(); // re-register after change
    }

    private void RefreshCacheIfEmpty()
    {
        if (PublishersCache.IsEmpty || PublishersByGuidCache.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        var latestPublishers = publishersReader.GetAll(_lastModified);
        foreach (var record in latestPublishers)
        {
            PublishersCache[record.Id] = record;

            PublishersByGuidCache[record.PublisherRef] = record;

            if (record.LastModified > _lastModified)
            {
                _lastModified = record.LastModified;
            }
        }
    }
}
