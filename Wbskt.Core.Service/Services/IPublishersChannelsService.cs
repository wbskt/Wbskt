using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IPublishersChannelsService
{
    void AddPublisherToChannel(int publisherId, int channelId);
    void RemovePublisherFromChannel(int publisherId, int channelId);
    void AddChannelsToPublisher(int publisherId, int[] channelIds);
    bool RemovePublishersFromChannel(Guid[] publisherRefs, Guid channelRef);
    bool AddPublishersToChannel(Guid[] publisherRefs, Guid channelRef);
    void RemoveChannelsFromPublisher(int[] publisherIds, int channelId);
    void BulkUpsertPublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs);
    void BulkDeletePublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs);
    IReadOnlyCollection<ChannelReadRecord> GetChannelsForPublisherRef(Guid publisherRef);
    IReadOnlyCollection<PublisherReadRecord> GetPublishersForChannelRef(Guid channelRef);
    IReadOnlyCollection<PublisherChannels> GetChannelsForPublishers(Guid[] publisherRefs);
    IReadOnlyCollection<ChannelPublishers> GetPublishersForChannels(Guid[] channelRefs);
}
