using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

public class PublishersChannelsService(
    ILogger<PublishersChannelsService> logger,
    IPublishersChannelsWriter publishersChannelsWriter,
    IPublishersChannelsReader publishersChannelsReader,
    IPublishersReader publishersReader,
    IChannelsReader channelsReader
    ) : IPublishersChannelsService
{
    public bool AddPublishersToChannel(Guid[] publisherRefs, Guid channelRef)
    {
        ArgumentNullException.ThrowIfNull(publisherRefs);
        
        try
        {
            logger.LogDebug("adding {publisherCount} publishers to channel {channelRef}", publisherRefs.Length, channelRef);

            var channel = channelsReader.GetByRef(channelRef);
            var publishers = publishersReader.GetAllByRefs(publisherRefs);

            if (publishers.Count == 0)
            {
                logger.LogWarning("no valid publishers found for the provided publisher refs");
                return false;
            }

            var pairs = publishers.Select(publisher => new PublisherChannelRecord { PublisherId = publisher.Id, ChannelId = channel.Id }).ToArray();
            publishersChannelsWriter.BulkUpsertPublisherChannels(pairs);

            logger.LogDebug("successfully added {publisherCount} publishers to channel {channelRef}", publishers.Count, channelRef);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to add publishers to channel {channelRef}", channelRef);
            return false;
        }
    }

    public bool RemovePublishersFromChannel(Guid[] publisherRefs, Guid channelRef)
    {
        ArgumentNullException.ThrowIfNull(publisherRefs);
        
        try
        {
            logger.LogDebug("removing {publisherCount} publishers from channel {channelRef}", publisherRefs.Length, channelRef);

            var channel = channelsReader.GetByRef(channelRef);
            var publishers = publishersReader.GetAllByRefs(publisherRefs);

            if (publishers.Count == 0)
            {
                logger.LogWarning("no valid publishers found for the provided publisher refs");
                return false;
            }

            var pairs = publishers.Select(publisher => new PublisherChannelRecord { PublisherId = publisher.Id, ChannelId = channel.Id }).ToArray();
            publishersChannelsWriter.BulkDeletePublisherChannels(pairs);

            logger.LogDebug("successfully removed {publisherCount} publishers from channel {channelRef}", publishers.Count, channelRef);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to remove publishers from channel {channelRef}", channelRef);
            return false;
        }
    }

    public bool AddChannelsToPublisher(Guid[] channelRefs, Guid publisherRef)
    {
        ArgumentNullException.ThrowIfNull(channelRefs);
        
        try
        {
            logger.LogDebug("adding {channelCount} channels to publisher {publisherRef}", channelRefs.Length, publisherRef);

            var publisher = publishersReader.GetByRef(publisherRef);
            var channels = channelsReader.GetAllByRefs(channelRefs);

            if (channels.Count == 0)
            {
                logger.LogWarning("no valid channels found for the provided channel refs");
                return false;
            }

            var pairs = channels.Select(channel => new PublisherChannelRecord { PublisherId = publisher.Id, ChannelId = channel.Id }).ToArray();
            publishersChannelsWriter.BulkUpsertPublisherChannels(pairs);

            logger.LogDebug("successfully added {channelCount} channels to publisher {publisherRef}", channels.Count, publisherRef);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to add channels to publisher {publisherRef}", publisherRef);
            return false;
        }
    }

    public bool RemoveChannelsFromPublisher(Guid[] channelRefs, Guid publisherRef)
    {
        ArgumentNullException.ThrowIfNull(channelRefs);
        
        try
        {
            logger.LogDebug("removing {channelCount} channels from publisher {publisherRef}", channelRefs.Length, publisherRef);

            var publisher = publishersReader.GetByRef(publisherRef);
            var channels = channelsReader.GetAllByRefs(channelRefs);

            if (channels.Count == 0)
            {
                logger.LogWarning("no valid channels found for the provided channel refs");
                return false;
            }

            var pairs = channels.Select(channel => new PublisherChannelRecord { PublisherId = publisher.Id, ChannelId = channel.Id }).ToArray();
            publishersChannelsWriter.BulkDeletePublisherChannels(pairs);

            logger.LogDebug("successfully removed {channelCount} channels from publisher {publisherRef}", channels.Count, publisherRef);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to remove channels from publisher {publisherRef}", publisherRef);
            return false;
        }
    }

    public IReadOnlyCollection<ChannelReadRecord> GetChannelsForPublisherRef(Guid publisherRef)
    {
        var publisher = publishersReader.GetByRef(publisherRef);
        var channelIds = GetChannelIdsForPublisher(publisher.Id);
        return channelsReader.GetAllByIds(channelIds.ToArray());
    }

    public IReadOnlyCollection<PublisherReadRecord> GetPublishersForChannelRef(Guid channelRef)
    {
        var channel = channelsReader.GetByRef(channelRef);
        var publisherIds = GetPublisherIdsForChannel(channel.Id);
        return publishersReader.GetAllByIds(publisherIds.ToArray());
    }

    public IReadOnlyCollection<PublisherChannels> GetChannelsForPublishers(Guid[] publisherRefs)
    {
        ArgumentNullException.ThrowIfNull(publisherRefs);
        
        if (publisherRefs.Length == 0)
            return [];

        // Get all publishers in one call
        var publishers = publishersReader.GetAllByRefs(publisherRefs);
        var publisherIds = publishers.Select(p => p.Id).ToArray();
        
        // Get all channel IDs for all publishers in one call
        var allChannelIds = publishersChannelsReader.GetChannelIdsForPublishers(publisherIds);
        var allChannels = channelsReader.GetAllByIds(allChannelIds.ToArray());
        
        // Create a lookup for channels by ID
        var channelsById = allChannels.ToDictionary(c => c.Id);
        
        var result = new List<PublisherChannels>();
        foreach (var publisher in publishers)
        {
            var channelIds = publishersChannelsReader.GetChannelIdsForPublisher(publisher.Id);
            var channels = channelIds.Where(id => channelsById.ContainsKey(id))
                                   .Select(id => channelsById[id])
                                   .ToArray();
            
            result.Add(new PublisherChannels
            {
                PublisherRef = publisher.PublisherRef,
                ChannelRefs = channels.Select(c => c.ChannelRef).ToArray()
            });
        }

        return result.AsReadOnly();
    }

    public IReadOnlyCollection<ChannelPublishers> GetPublishersForChannels(Guid[] channelRefs)
    {
        ArgumentNullException.ThrowIfNull(channelRefs);
        
        if (channelRefs.Length == 0)
            return [];

        // Get all channels in one call
        var channels = channelsReader.GetAllByRefs(channelRefs);
        var channelIds = channels.Select(c => c.Id).ToArray();
        
        // Get all publisher IDs for all channels in one call
        var allPublisherIds = publishersChannelsReader.GetPublisherIdsForChannels(channelIds);
        var allPublishers = publishersReader.GetAllByIds(allPublisherIds.ToArray());
        
        // Create a lookup for publishers by ID
        var publishersById = allPublishers.ToDictionary(p => p.Id);
        
        var result = new List<ChannelPublishers>();
        foreach (var channel in channels)
        {
            var publisherIds = publishersChannelsReader.GetPublisherIdsForChannel(channel.Id);
            var publishers = publisherIds.Where(id => publishersById.ContainsKey(id))
                                       .Select(id => publishersById[id])
                                       .ToArray();
            
            result.Add(new ChannelPublishers
            {
                ChannelRef = channel.ChannelRef,
                PublisherRefs = publishers.Select(p => p.PublisherRef).ToArray()
            });
        }

        return result.AsReadOnly();
    }

    public IReadOnlyCollection<int> GetChannelIdsForPublisher(int publisherId)
    {
        return publishersChannelsReader.GetChannelIdsForPublisher(publisherId);
    }

    public IReadOnlyCollection<int> GetPublisherIdsForChannel(int channelId)
    {
        return publishersChannelsReader.GetPublisherIdsForChannel(channelId);
    }
}
