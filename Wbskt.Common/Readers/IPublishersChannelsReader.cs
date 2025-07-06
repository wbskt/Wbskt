namespace Wbskt.Common.Readers;

public interface IPublishersChannelsReader
{
    IReadOnlyCollection<int> GetChannelIdsForPublisher(int publisherId);
    IReadOnlyCollection<int> GetPublisherIdsForChannel(int channelId);
} 