using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IChannelsService
{
    int Create(ChannelRecord channelRecord);

    IReadOnlyCollection<ChannelReadRecord> GetAllForUser(int userId);

    int GetChannelIdBySubscriptionRef(Guid subscriptionRef);

    Guid GetChannelSubscriptionRefById(int channelId);

    IReadOnlyCollection<int> GetChannelIdsBySubscriptionRefs(Guid[] subscriptionRefs);

    IReadOnlyCollection<Guid> GetChannelSubscriptionRefsByIds(int[] channelIds);
}
