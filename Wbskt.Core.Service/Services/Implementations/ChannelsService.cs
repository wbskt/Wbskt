using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

public class ChannelsService(ILogger<ChannelsService> logger, IChannelsWriter channelsWriter, IChannelsReader channelsReader) : IChannelsService
{
    public int CreateChannel(ChannelRecord channelRecord)
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

    public IReadOnlyCollection<ChannelRecord> GetChannelsForUser(int userId)
    {
        return channelsReader.GetAllByUserId(userId);
    }

    private bool CheckIfUserHasSameChannelName(int userId, string channelName)
    {
        var channels = GetChannelsForUser(userId);
        return channels.Any(c => c.UserId == userId && c.Name == channelName);
    }
}
