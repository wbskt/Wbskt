using Microsoft.Extensions.Logging;
using Wbskt.Common.Contracts;
using Wbskt.Common.Exceptions;

namespace Wbskt.Common.Providers.Cache;

internal sealed class CachedChannelsProvider(ILogger<CachedChannelsProvider> logger, IChannelsProvider channelsProvider) : ICachedChannelsProvider
{
    private static readonly string ServerType = Environment.GetEnvironmentVariable(nameof(ServerType)) ?? Constants.ServerType.CoreServer.ToString();

    private readonly List<ChannelDetails> channels = [];
    private readonly object @lock = new();

    public IReadOnlyCollection<ChannelDetails> GetAllByChannelPublisherId(Guid channelPublisherId)
    {
        return [.. channels.Where(c => c.ChannelPublisherId == channelPublisherId)];
    }

    public IReadOnlyCollection<ChannelDetails> GetAllByChannelUserId(int userId)
    {
        return [.. channels.Where(c => c.UserId == userId)];
    }

    public IReadOnlyCollection<ChannelDetails> GetAll()
    {
        if (ServerType != Constants.ServerType.CoreServer.ToString())
        {
            logger.LogError("only core server perform this operation: {operationName}", nameof(GetAll));
            return [];
        }

        lock (@lock)
        {
            if (channels.Count != 0)
            {
                return [.. channels]; // return a copy to prevent external mutation
            }

            var records = channelsProvider.GetAll();
            channels.AddRange(records);

            return [.. channels]; // return a copy to prevent external mutation
        }
    }

    public ChannelDetails GetByChannelSubscriberId(Guid channelSubscriberId)
    {
        return channels.FirstOrDefault(c => c.ChannelSubscriberId == channelSubscriberId) ?? throw WbsktExceptions.ChannelSubscriberIdNotExists(channelSubscriberId);
    }

    public ChannelDetails GetByChannelId(int channelId)
    {
        return channels.FirstOrDefault(c => c.ChannelId == channelId) ?? throw WbsktExceptions.ChannelIdNotExists(channelId);
    }

    public int CreateChannel(ChannelDetails channel)
    {
        if (ServerType != Constants.ServerType.CoreServer.ToString())
        {
            logger.LogError("only core server perform this operation: {operationName}", nameof(CreateChannel));
            return -1;
        }

        var id = channelsProvider.CreateChannel(channel);
        if (id <= 0)
        {
            throw new InvalidOperationException("this shouldn't happen. DB returned with id <= 0. (seek help)");
        }

        lock (@lock)
        {
            channels.Add(channel);
        }
        return id;
    }

    public IReadOnlyCollection<ChannelDetails> GetAllByChannelSubscriberIds(Guid[] channelSubscriberIds)
    {
        return [..channels.Where(c => channelSubscriberIds.Contains(c.ChannelSubscriberId))];
    }
}
