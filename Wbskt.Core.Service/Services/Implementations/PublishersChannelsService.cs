using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

public class PublishersChannelsService(ILogger<PublishersChannelsService> logger, IPublishersChannelsWriter publishersChannelsWriter, IPublishersChannelsReader publishersChannelsReader) : IPublishersChannelsService
{
    public void AddPublisherToChannel(int publisherId, int channelId)
    {
        logger.LogDebug("adding publisher {publisherId} to channel {channelId}", publisherId, channelId);

        // Check if relation already exists
        var existingChannels = GetChannelIdsForPublisher(publisherId);
        if (existingChannels.Contains(channelId))
        {
            logger.LogDebug("publisher {publisherId} is already in channel {channelId}", publisherId, channelId);
            return;
        }

        publishersChannelsWriter.UpsertPublisherChannel(publisherId, channelId);
        logger.LogDebug("successfully added publisher {publisherId} to channel {channelId}", publisherId, channelId);
    }

    public void RemovePublisherFromChannel(int publisherId, int channelId)
    {
        logger.LogDebug("removing publisher {publisherId} from channel {channelId}", publisherId, channelId);

        // Check if relation exists
        var existingChannels = GetChannelIdsForPublisher(publisherId);
        if (!existingChannels.Contains(channelId))
        {
            logger.LogDebug("publisher {publisherId} is not in channel {channelId}", publisherId, channelId);
            return;
        }

        publishersChannelsWriter.DeletePublisherChannel(publisherId, channelId);
        logger.LogDebug("successfully removed publisher {publisherId} from channel {channelId}", publisherId, channelId);
    }

    public IReadOnlyCollection<int> GetChannelIdsForPublisher(int publisherId)
    {
        return publishersChannelsReader.GetChannelIdsForPublisher(publisherId);
    }

    public IReadOnlyCollection<int> GetPublisherIdsForChannel(int channelId)
    {
        return publishersChannelsReader.GetPublisherIdsForChannel(channelId);
    }

    public void AddPublishersToChannel(int publisherId, int[] channelIds)
    {
        logger.LogDebug("adding publisher {publisherId} to {channelCount} channels", publisherId, channelIds.Length);

        var pairs = channelIds.Select(channelId => new PublisherChannelRecord { PublisherId = publisherId, ChannelId = channelId }).ToList();
        BulkUpsertPublisherChannels(pairs);
    }

    public void RemovePublishersFromChannel(int publisherId, int[] channelIds)
    {
        logger.LogDebug("removing publisher {publisherId} from {channelCount} channels", publisherId, channelIds.Length);

        var pairs = channelIds.Select(channelId => new PublisherChannelRecord { PublisherId = publisherId, ChannelId = channelId }).ToList();
        BulkDeletePublisherChannels(pairs);
    }

    public void AddChannelsToPublisher(int[] publisherIds, int channelId)
    {
        logger.LogDebug("adding {publisherCount} publishers to channel {channelId}", publisherIds.Length, channelId);

        var pairs = publisherIds.Select(publisherId => new PublisherChannelRecord { PublisherId = publisherId, ChannelId = channelId }).ToList();
        BulkUpsertPublisherChannels(pairs);
    }

    public void RemoveChannelsFromPublisher(int[] publisherIds, int channelId)
    {
        logger.LogDebug("removing {publisherCount} publishers from channel {channelId}", publisherIds.Length, channelId);

        var pairs = publisherIds.Select(publisherId => new PublisherChannelRecord { PublisherId = publisherId, ChannelId = channelId }).ToList();
        BulkDeletePublisherChannels(pairs);
    }

    public void BulkUpsertPublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs)
    {
        logger.LogDebug("bulk upserting {count} publisher-channel relations", pairs.Count);
        publishersChannelsWriter.BulkUpsertPublisherChannels(pairs);
    }

    public void BulkDeletePublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs)
    {
        logger.LogDebug("bulk deleting {count} publisher-channel relations", pairs.Count);
        publishersChannelsWriter.BulkDeletePublisherChannels(pairs);
    }

    public IReadOnlyCollection<int> GetChannelIdsForPublishers(int[] publisherIds)
    {
        return publishersChannelsReader.GetChannelIdsForPublishers(publisherIds);
    }

    public IReadOnlyCollection<int> GetPublisherIdsForChannels(int[] channelIds)
    {
        return publishersChannelsReader.GetPublisherIdsForChannels(channelIds);
    }
}
