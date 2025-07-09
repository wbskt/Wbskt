using System.Collections.Concurrent;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedPublishersChannelsReader(ILogger<CachedPublishersChannelsReader> logger, IPublishersChannelsDatabaseReader publishersChannelsReader)
    : IDatabaseChangeListener, IPublishersChannelsReader
{
    private static DateTime _lastModified = DateTime.UnixEpoch;
    private static readonly ConcurrentDictionary<int, List<PublisherChannelReadRecord>> ChannelToPublishers = [];
    private static readonly ConcurrentDictionary<int, List<PublisherChannelReadRecord>> PublisherToChannels = [];

    public void RegisterDatabaseListener()
    {
        if (publishersChannelsReader is PublishersChannelsDatabaseReader publishersChannelsReaderImp)
        {
            publishersChannelsReaderImp.RegisterSqlDependency(OnDatabaseChange);
        }
    }

    public IReadOnlyCollection<int> GetChannelIdsForPublisher(int publisherId)
    {
        RefreshCacheIfEmpty();
        if (PublisherToChannels.TryGetValue(publisherId, out var channels))
        {
            return [.. channels.Where(pc => !pc.Deleted).Select(pc => pc.ChannelId)];
        }

        return [];
    }

    public IReadOnlyCollection<int> GetPublisherIdsForChannel(int channelId)
    {
        RefreshCacheIfEmpty();
        if (ChannelToPublishers.TryGetValue(channelId, out var publishers))
        {
            return [.. publishers.Where(pc => !pc.Deleted).Select(pc => pc.PublisherId)];
        }

        return [];
    }

    public IReadOnlyCollection<int> GetChannelIdsForPublishers(int[] publisherIds)
    {
        RefreshCacheIfEmpty();
        var allChannelIds = new HashSet<int>();

        foreach (var publisherId in publisherIds)
        {
            if (PublisherToChannels.TryGetValue(publisherId, out var channels))
            {
                foreach (var channel in channels.Where(pc => !pc.Deleted))
                {
                    allChannelIds.Add(channel.ChannelId);
                }
            }
        }

        return allChannelIds.ToList().AsReadOnly();
    }

    public IReadOnlyCollection<int> GetPublisherIdsForChannels(int[] channelIds)
    {
        RefreshCacheIfEmpty();
        var allPublisherIds = new HashSet<int>();

        foreach (var channelId in channelIds)
        {
            if (ChannelToPublishers.TryGetValue(channelId, out var publishers))
            {
                foreach (var publisher in publishers.Where(pc => !pc.Deleted))
                {
                    allPublisherIds.Add(publisher.PublisherId);
                }
            }
        }

        return allPublisherIds.ToList().AsReadOnly();
    }

    private void OnDatabaseChange(object sender, SqlNotificationEventArgs e)
    {
        logger.LogInformation("database change detected: {Info}", e.Info);

        RegisterDatabaseListener(); // re-register after change
        RefreshCache();
    }

    private void RefreshCacheIfEmpty()
    {
        if (PublisherToChannels.IsEmpty || ChannelToPublishers.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        var latestPublisherChannels = publishersChannelsReader.GetAll(_lastModified);
        foreach (var record in latestPublisherChannels)
        {
            // Add to publisher -> channels mapping
            PublisherToChannels.AddOrUpdate(
                record.PublisherId,
                [record],
                (_, channels) =>
                {
                    var existingIndex = channels.FindIndex(pc => pc.ChannelId == record.ChannelId);
                    if (existingIndex >= 0)
                    {
                        channels[existingIndex] = record; // Update existing - same object reference
                    }
                    else
                    {
                        channels.Add(record); // Add new - same object reference
                    }

                    return channels;
                }
            );

            // Add to channel -> publishers mapping (same record object)
            ChannelToPublishers.AddOrUpdate(
                record.ChannelId,
                [record],
                (_, publishers) =>
                {
                    var existingIndex = publishers.FindIndex(pc => pc.PublisherId == record.PublisherId);
                    if (existingIndex >= 0)
                    {
                        publishers[existingIndex] = record; // Update existing - same object reference
                    }
                    else
                    {
                        publishers.Add(record); // Add new - same object reference
                    }

                    return publishers;
                }
            );

            if (record.LastModified > _lastModified)
            {
                _lastModified = record.LastModified;
            }
        }
    }
}
