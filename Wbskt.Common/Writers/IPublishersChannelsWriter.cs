using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IPublishersChannelsWriter
{
    void UpsertPublisherChannel(int publisherId, int channelId);
    void DeletePublisherChannel(int publisherId, int channelId);
    void BulkUpsertPublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs);
    void BulkDeletePublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs);
}
