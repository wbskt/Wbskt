using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IClientsChannelsWriter
{
    void UpsertClientChannel(int clientId, int channelId);
    void DeleteClientChannel(int clientId, int channelId);
    void BulkUpsertClientChannels(IReadOnlyCollection<ClientChannelRecord> pairs);
    void BulkDeleteClientChannels(IReadOnlyCollection<ClientChannelRecord> pairs);
}
