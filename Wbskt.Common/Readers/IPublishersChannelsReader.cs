namespace Wbskt.Common.Readers;

public interface IPublishersChannelsReader
{
    IReadOnlyCollection<int> GetChannelIdsForPublisher(int publisherId);
    IReadOnlyCollection<int> GetPublisherIdsForChannel(int channelId);
    IReadOnlyCollection<int> GetChannelIdsForPublishers(int[] publisherIds);
    IReadOnlyCollection<int> GetPublisherIdsForChannels(int[] channelIds);
}