

using Wbskt.Common.Contracts;
using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IChannelsService
{
    int CreateChannel(ChannelRecord channelRecord);

    IEnumerable<ChannelRecord> GetChannelsForUser(int userId);
}
