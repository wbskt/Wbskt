using System.Collections.Concurrent;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedChannelsReader(ILogger<CachedChannelsReader> logger, IChannelsDatabaseReader channelsReader) : IChannelsReader, IDatabaseChangeListener
{
    private static DateTime _lastModified = DateTime.MinValue;
    private readonly ConcurrentDictionary<int, ChannelReadRecord> channelsCache = [];
    private readonly ConcurrentDictionary<Guid, ChannelReadRecord> channelsByGuidCache = [];

    public ChannelReadRecord GetByChannelId(int channelId)
    {
        RefreshCacheIfEmpty();
        if (channelsCache.TryGetValue(channelId, out var record))
        {
            return record;
        }

        throw WbsktExceptions.ChannelIdNotExists(channelId);
    }

    public ChannelReadRecord GetByChannelSubscriberRef(Guid subscriberRef)
    {
        RefreshCacheIfEmpty();
        if (channelsByGuidCache.TryGetValue(subscriberRef, out var record))
        {
            return record;
        }

        throw WbsktExceptions.ChannelSubscriberIdNotExists(subscriberRef);
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAll()
    {
        RefreshCacheIfEmpty();
        return [.. channelsCache.Values];
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAllByUserId(int userId)
    {
        RefreshCacheIfEmpty();
        return [.. channelsCache.Values.Where(c => c.UserId == userId)];
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAllByIds(int[] ids)
    {
        RefreshCacheIfEmpty();
        var result = new List<ChannelReadRecord>();
        
        foreach (var id in ids)
        {
            if (channelsCache.TryGetValue(id, out var record))
            {
                result.Add(record);
            }
        }
        
        return result.AsReadOnly();
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAllBySubscriberRefs(Guid[] subscriberRefs)
    {
        RefreshCacheIfEmpty();
        var result = new List<ChannelReadRecord>();
        
        foreach (var subscriberRef in subscriberRefs)
        {
            if (channelsByGuidCache.TryGetValue(subscriberRef, out var record))
            {
                result.Add(record);
            }
        }
        
        return result.AsReadOnly();
    }

    public void RegisterDatabaseListener()
    {
        if (channelsReader is ChannelsDatabaseReader channelsReaderImp)
        {
            channelsReaderImp.RegisterSqlDependency(OnDatabaseChange);
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
        if (channelsCache.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        var latestChannels = channelsReader.GetAll(_lastModified);
        foreach (var record in latestChannels)
        {
            if (channelsCache.TryGetValue(record.Id, out _))
            {
                channelsCache[record.Id] = record;
            }
            else
            {
                channelsCache[record.Id] = record;
            }

            // Update GUID cache
            channelsByGuidCache[record.SubscriptionRef] = record;

            if (record.LastModified > _lastModified)
            {
                _lastModified = record.LastModified;
            }
        }
    }
}
