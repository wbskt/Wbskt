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
    private static DateTime _lastModified = DateTime.MinValue;
    private readonly ConcurrentDictionary<int, PublisherReadRecord> publishersCache = [];
    private readonly ConcurrentDictionary<Guid, PublisherReadRecord> publishersByGuidCache = [];

    public PublisherReadRecord GetById(int id)
    {
        RefreshCacheIfEmpty();
        if (publishersCache.TryGetValue(id, out var record))
        {
            return record;
        }

        throw WbsktExceptions.PublisherIdNotExists(id);
    }

    public PublisherReadRecord GetByPublisherRef(Guid publisherRef)
    {
        RefreshCacheIfEmpty();
        if (publishersByGuidCache.TryGetValue(publisherRef, out var record))
        {
            return record;
        }

        throw WbsktExceptions.PublisherRefNotExists(publisherRef);
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAll()
    {
        RefreshCacheIfEmpty();
        return [.. publishersCache.Values];
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAllByUserId(int userId)
    {
        RefreshCacheIfEmpty();
        return [.. publishersCache.Values.Where(c => c.UserId == userId)];
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAllByIds(int[] ids)
    {
        RefreshCacheIfEmpty();
        var result = new List<PublisherReadRecord>();
        
        foreach (var id in ids)
        {
            if (publishersCache.TryGetValue(id, out var record))
            {
                result.Add(record);
            }
        }
        
        return result.AsReadOnly();
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAllByPublisherRefs(Guid[] publisherRefs)
    {
        RefreshCacheIfEmpty();
        var result = new List<PublisherReadRecord>();
        
        foreach (var publisherRef in publisherRefs)
        {
            if (publishersByGuidCache.TryGetValue(publisherRef, out var record))
            {
                result.Add(record);
            }
        }
        
        return result.AsReadOnly();
    }

    public void RegisterDatabaseListener()
    {
        if (publishersReader is PublishersDatabaseReader publishersReaderImp)
        {
            publishersReaderImp.RegisterSqlDependency(OnDatabaseChange);
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
        if (publishersCache.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        var latestPublishers = publishersReader.GetAll(_lastModified);
        foreach (var record in latestPublishers)
        {
            if (publishersCache.TryGetValue(record.Id, out _))
            {
                publishersCache[record.Id] = record;
            }
            else
            {
                publishersCache[record.Id] = record;
            }

            // Update GUID cache
            publishersByGuidCache[record.PublisherRef] = record;

            if (record.LastModified > _lastModified)
            {
                _lastModified = record.LastModified;
            }
        }
    }
}
