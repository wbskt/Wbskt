using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IChannelsService
{
    int Create(ChannelRecord channelRecord);

    IReadOnlyCollection<ChannelReadRecord> GetAllForUser(int userId);
}
