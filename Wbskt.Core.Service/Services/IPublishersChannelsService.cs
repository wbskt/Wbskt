using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IPublishersChannelsService
{
    void AddPublisherToChannel(int publisherId, int channelId);
    void RemovePublisherFromChannel(int publisherId, int channelId);
    void AddPublishersToChannel(int publisherId, int[] channelIds);
    void RemovePublishersFromChannel(int publisherId, int[] channelIds);
    void AddChannelsToPublisher(int[] publisherIds, int channelId);
    void RemoveChannelsFromPublisher(int[] publisherIds, int channelId);
    void BulkUpsertPublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs);
    void BulkDeletePublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs);
    IReadOnlyCollection<int> GetChannelIdsForPublisher(int publisherId);
    IReadOnlyCollection<int> GetPublisherIdsForChannel(int channelId);
    IReadOnlyCollection<int> GetChannelIdsForPublishers(int[] publisherIds);
    IReadOnlyCollection<int> GetPublisherIdsForChannels(int[] channelIds);
}
