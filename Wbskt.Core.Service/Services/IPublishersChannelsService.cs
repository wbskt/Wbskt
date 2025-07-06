using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IPublishersChannelsService
{
    bool RemovePublishersFromChannel(Guid[] publisherRefs, Guid channelRef);
    bool AddPublishersToChannel(Guid[] publisherRefs, Guid channelRef);
    bool AddChannelsToPublisher(Guid[] channelRefs, Guid publisherRef);
    bool RemoveChannelsFromPublisher(Guid[] channelRefs, Guid publisherRef);
    IReadOnlyCollection<ChannelReadRecord> GetChannelsForPublisherRef(Guid publisherRef);
    IReadOnlyCollection<PublisherReadRecord> GetPublishersForChannelRef(Guid channelRef);
    IReadOnlyCollection<PublisherChannels> GetChannelsForPublishers(Guid[] publisherRefs);
    IReadOnlyCollection<ChannelPublishers> GetPublishersForChannels(Guid[] channelRefs);
}
