namespace Wbskt.Common.Readers;

public interface IClientsChannelsReader
{
    IReadOnlyCollection<int> GetChannelIdsForClient(int clientId);
    IReadOnlyCollection<int> GetClientIdsForChannel(int channelId);
    IReadOnlyCollection<int> GetChannelIdsForClients(int[] clientIds);
    IReadOnlyCollection<int> GetClientIdsForChannels(int[] channelIds);
}
