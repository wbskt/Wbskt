using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

public class ChannelsService(ILogger<ChannelsService> logger, IChannelsWriter channelsWriter, IChannelsReader channelsReader) : IChannelsService
{
    public int Create(ChannelRecord channelRecord)
    {
        if (CheckIfUserHasSameChannelName(channelRecord.UserId, channelRecord.Name))
        {
            throw WbsktExceptions.ChannelExists(channelRecord.Name);
        }

        channelRecord.SubscriptionRef = Guid.NewGuid();
        var id = channelsWriter.InsertChannel(channelRecord);
        logger.LogDebug("channel created with SubscriptionRef: {SubscriptionRef}", channelRecord.SubscriptionRef);
        return id;
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAllForUser(int userId)
    {
        return channelsReader.GetAllByUserId(userId);
    }

    public ChannelReadRecord GetByRef(Guid subscriptionRef)
    {
        var channel = channelsReader.GetByRef(subscriptionRef);
        return channel;
    }

    public ChannelReadRecord GetById(int channelId)
    {
        var channel = channelsReader.GetById(channelId);
        return channel;
    }

    public IReadOnlyCollection<int> GetChannelIdsBySubscriptionRefs(Guid[] subscriptionRefs)
    {
        var channelIds = new List<int>();

        foreach (var subscriptionRef in subscriptionRefs)
        {
            try
            {
                var channel = GetByRef(subscriptionRef);
                channelIds.Add(channel.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Channel with subscription ref '{SubscriptionRef}' not found", subscriptionRef);
            }
        }

        return channelIds.AsReadOnly();
    }

    public IReadOnlyCollection<Guid> GetChannelSubscriptionRefsByIds(int[] channelIds)
    {
        var subscriptionRefs = new List<Guid>();

        foreach (var channelId in channelIds)
        {
            try
            {
                var channel = GetById(channelId);
                subscriptionRefs.Add(channel.SubscriptionRef);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Channel with ID '{ChannelId}' not found", channelId);
            }
        }

        return subscriptionRefs.AsReadOnly();
    }

    private bool CheckIfUserHasSameChannelName(int userId, string channelName)
    {
        var channels = GetAllForUser(userId);
        return channels.Any(c => c.UserId == userId && c.Name == channelName);
    }
}
