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

    public ChannelReadRecord GetByChannelId(int channelId)
    {
        RefreshCacheIfEmpty();
        if (channelsCache.TryGetValue(channelId, out var record))
        {
            return record;
        }

        throw WbsktExceptions.ChannelIdNotExists(channelId);
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

    public IReadOnlyCollection<ChannelReadRecord> GetAllByPublisherRef(Guid publisherRef)
    {
        RefreshCacheIfEmpty();
        throw new NotImplementedException();
    }

    public ChannelReadRecord GetByChannelSubscriberRef(Guid subscriberRef)
    {
        RefreshCacheIfEmpty();
        return channelsCache.Values.FirstOrDefault(c => c.SubscriptionRef == subscriberRef) ?? throw WbsktExceptions.ChannelSubscriberIdNotExists(subscriberRef);
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAllBySubscriberRefs(Guid[] subscriberRefs)
    {
        RefreshCacheIfEmpty();
        return [.. channelsCache.Values.Where(c => subscriberRefs.Contains(c.SubscriptionRef))];
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

            if (record.LastModified > _lastModified)
            {
                _lastModified = record.LastModified;
            }
        }
    }
}
