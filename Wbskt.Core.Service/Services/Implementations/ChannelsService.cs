using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class ChannelsService(ILogger<ChannelsService> logger, IChannelsWriter channelsWriter, IChannelsReader channelsReader) : IChannelsService
{
    public int Create(ChannelRecord channelRecord)
    {
        if (CheckIfUserHasSameChannelName(channelRecord.UserId, channelRecord.Name))
        {
            throw WbsktExceptions.ChannelExists(channelRecord.Name);
        }

        channelRecord.ChannelRef = Guid.NewGuid();
        var id = channelsWriter.InsertChannel(channelRecord);
        logger.LogDebug("channel created with ChannelRef: {ChannelRef}", channelRecord.ChannelRef);
        return id;
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAllForUser(int userId)
    {
        return channelsReader.GetAllByUserId(userId);
    }

    private bool CheckIfUserHasSameChannelName(int userId, string channelName)
    {
        var channels = GetAllForUser(userId);
        return channels.Any(c => c.UserId == userId && c.Name == channelName);
    }
}
