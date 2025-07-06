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

    public ChannelReadRecord GetById(int channelId)
    {
        RefreshCacheIfEmpty();
        if (channelsCache.TryGetValue(channelId, out var record))
        {
            return record;
        }

        throw WbsktExceptions.ChannelIdNotExists(channelId);
    }

    public ChannelReadRecord GetByRef(Guid channelRef)
    {
        RefreshCacheIfEmpty();
        if (channelsByGuidCache.TryGetValue(channelRef, out var record))
        {
            return record;
        }

        throw WbsktExceptions.ChannelRefNotExists(channelRef);
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
        var result = new List<ChannelReadRecord>(ids.Length);

        foreach (var id in ids)
        {
            if (channelsCache.TryGetValue(id, out var record))
            {
                result.Add(record);
            }
        }

        return result.AsReadOnly();
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAllByRefs(Guid[] channelRefs)
    {
        RefreshCacheIfEmpty();
        var result = new List<ChannelReadRecord>(channelRefs.Length);

        foreach (var channelRef in channelRefs)
        {
            if (channelsByGuidCache.TryGetValue(channelRef, out var record))
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
        if (channelsCache.IsEmpty || channelsByGuidCache.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        var latestChannels = channelsReader.GetAll(_lastModified);
        foreach (var record in latestChannels)
        {
            channelsCache[record.Id] = record;
            channelsByGuidCache[record.ChannelRef] = record;

            if (record.LastModified > _lastModified)
            {
                _lastModified = record.LastModified;
            }
        }
    }
}
