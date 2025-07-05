using Wbskt.Common.Exceptions;
using Wbskt.Common.Providers.Readers;
using Wbskt.Common.Providers.Writers;
using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services.Implementations;

public class ChannelsService(ILogger<ChannelsService> logger, IChannelsWriter channelsWriter, IChannelsDatabaseReader channelsReader) : IChannelsService
{
    public int CreateChannel(ChannelRecord channelRecord)
    {
        if (CheckIfUserHasSameChannelName(channelRecord.UserId, channelRecord.Name))
        {
            throw WbsktExceptions.ChannelExists(channelRecord.Name);
        }

        channelRecord.SubscriptionRef = Guid.NewGuid();
        var id = channelsWriter.InsertChannel(channelRecord);
        return id;
    }

    public IEnumerable<ChannelRecord> GetChannelsForUser(int userId)
    {
        return channelsReader.GetAllByUserId(DateTime.MinValue, userId);
    }

    private bool CheckIfUserHasSameChannelName(int userId, string channelName)
    {
        var channels = GetChannelsForUser(userId);
        return channels.Any(c => c.UserId == userId && c.Name == channelName);
    }
}
