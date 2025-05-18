using Wbskt.Common.Contracts;

namespace Wbskt.Common.Providers;

public interface IChannelsProvider
{
    IReadOnlyCollection<ChannelDetails> GetAllByChannelPublisherId(Guid channelPublisherId);

    IReadOnlyCollection<ChannelDetails> GetAllByChannelUserId(int userId);

    IReadOnlyCollection<ChannelDetails> GetAll();

    ChannelDetails? GetByChannelSubscriberId(Guid channelSubscriberId);

    ChannelDetails? GetByChannelId(int channelId);

    int CreateChannel(ChannelDetails channel);
}

public interface ICachedChannelsProvider : IChannelsProvider
{
    IReadOnlyCollection<ChannelDetails> GetAllByChannelSubscriberIds(Guid[] channelSubscriberIds);
}
