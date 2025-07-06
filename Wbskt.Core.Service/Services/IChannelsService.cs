using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IChannelsService
{
    int Create(ChannelRecord channelRecord);

    IReadOnlyCollection<ChannelReadRecord> GetAllForUser(int userId);

    ChannelReadRecord GetByRef(Guid subscriptionRef);

    ChannelReadRecord GetById(int channelId);

    IReadOnlyCollection<int> GetChannelIdsBySubscriptionRefs(Guid[] subscriptionRefs);

    IReadOnlyCollection<Guid> GetChannelSubscriptionRefsByIds(int[] channelIds);
}
