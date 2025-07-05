using Wbskt.Common.Records;

namespace Wbskt.Common.Providers.Readers;

public interface IChannelsReader
{
    ChannelReadRecord GetByChannelId(int channelId);

    IReadOnlyCollection<ChannelReadRecord> GetAll();

    IReadOnlyCollection<ChannelReadRecord> GetAllByUserId(int userId);

    IReadOnlyCollection<ChannelReadRecord> GetAllByPublisherRef(Guid publisherRef);

    IReadOnlyCollection<ChannelReadRecord> GetAllBySubscriberRefs(Guid[] subscriberRefs);

    ChannelReadRecord GetByChannelSubscriberRef(Guid subscriberRef);
}
