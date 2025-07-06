using Microsoft.Extensions.Logging;
using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class ClientsManagementService(
    ILogger<ClientsManagementService> logger,
    IClientsReader clientsReader,
    IClientsWriter clientsWriter,
    IChannelsReader channelsReader,
    IClientsChannelsReader clientsChannelsReader,
    IClientsChannelsWriter clientsChannelsWriter) : IClientsManagementService
{
    public IReadOnlyCollection<ClientReadRecord> GetAll()
    {
        logger.LogTrace("getting all clients");
        return clientsReader.GetAll();
    }

    public IReadOnlyCollection<ClientReadRecord> GetAllByUserId(int userId)
    {
        logger.LogTrace("getting clients for user {userId}", userId);
        return clientsReader.GetAllByUserId(userId);
    }

    public ClientReadRecord GetByRef(Guid clientRef)
    {
        logger.LogTrace("getting client by ref {clientRef}", clientRef);
        return clientsReader.GetByRef(clientRef);
    }

    public IReadOnlyCollection<ClientReadRecord> GetAllByRefs(Guid[] clientRefs)
    {
        logger.LogTrace("getting clients by refs: {count} refs", clientRefs.Length);
        return clientsReader.GetAllByRefs(clientRefs);
    }

    public int UpsertClient(ClientRecord record)
    {
        logger.LogTrace("upserting client {name} with ref {ref}", record.Name, record.UniqueRef);
        return clientsWriter.UpsertClient(record);
    }

    public void SetClientChannels(Guid clientRef, Guid[] channelRefs)
    {
        logger.LogTrace("setting channels for client {clientRef}: {count} channels", clientRef, channelRefs.Length);

        // Get client ID from ref
        var client = clientsReader.GetByRef(clientRef);
        var clientId = client.Id;

        // Get channel IDs from refs
        var channels = channelsReader.GetAllByRefs(channelRefs);
        var channelIds = channels.Select(c => c.Id).ToArray();

        // Convert to records for bulk operation
        var clientChannelPairs = channelIds.Select(channelId => new ClientChannelRecord
        {
            ClientId = clientId,
            ChannelId = channelId
        }).ToList();

        // Perform bulk upsert
        clientsChannelsWriter.BulkUpsertClientChannels(clientChannelPairs);
        logger.LogDebug("set {count} channels for client {clientRef}", channelRefs.Length, clientRef);
    }

    public IReadOnlyCollection<Guid> GetChannelRefsForClient(Guid clientRef)
    {
        logger.LogTrace("getting channel refs for client {clientRef}", clientRef);

        // Get client ID from ref
        var client = clientsReader.GetByRef(clientRef);
        var clientId = client.Id;

        // Get channel IDs from client-channels relation
        var channelIds = clientsChannelsReader.GetChannelIdsForClient(clientId);

        // Get channel refs from IDs
        var channels = channelsReader.GetAllByIds(channelIds.ToArray());
        return channels.Select(c => c.ChannelRef).ToList().AsReadOnly();
    }

    public IReadOnlyCollection<Guid> GetClientRefsForChannel(Guid channelRef)
    {
        logger.LogTrace("getting client refs for channel {channelRef}", channelRef);

        // Get channel ID from ref
        var channel = channelsReader.GetByRef(channelRef);
        var channelId = channel.Id;

        // Get client IDs from client-channels relation
        var clientIds = clientsChannelsReader.GetClientIdsForChannel(channelId);

        // Get client refs from IDs
        var clients = clientsReader.GetAllByIds(clientIds.ToArray());
        return clients.Select(c => c.UniqueRef).ToList().AsReadOnly();
    }
} 