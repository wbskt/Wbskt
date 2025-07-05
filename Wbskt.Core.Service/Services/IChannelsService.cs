using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IChannelsService
{
    int CreateChannel(ChannelRecord channelRecord);

    IReadOnlyCollection<ChannelRecord> GetChannelsForUser(int userId);
}
