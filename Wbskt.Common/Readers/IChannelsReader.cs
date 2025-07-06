using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IChannelsReader
{
    ChannelReadRecord GetByChannelId(int channelId);

    IReadOnlyCollection<ChannelReadRecord> GetAll();

    IReadOnlyCollection<ChannelReadRecord> GetAllByUserId(int userId);

    IReadOnlyCollection<ChannelReadRecord> GetAllByIds(int[] ids);

    IReadOnlyCollection<ChannelReadRecord> GetAllBySubscriberRefs(Guid[] subscriberRefs);

    ChannelReadRecord GetByChannelSubscriberRef(Guid subscriberRef);
}
