using System.Collections.Concurrent;
using Wbskt.Common.Contracts;
using Wbskt.Common.Exceptions;
using Wbskt.Common.Providers;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Utilities;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class RelationService(
    ILogger<RelationService> logger,
    IClientsReader clientsReader,
    IClientsChannelsReader clientsChannelsReader,
    ICachedServerInfoProvider serverInfoProvider,
    IChannelsReader channelsReader) : IRelationService
{
    //<------------------------------->

    // channel - [clients]
    // used for: validation, n clients per channel, and look up for re-balancing
    private readonly ConcurrentDictionary<int, ConcurrentKeys<int>> channelClientsMap = new();

    // channel - [servers]
    // used for: dispatching messages to servers based on the channelId (gets the list of servers to which a message needs to be dispatched for a channel)
    private readonly ConcurrentDictionary<int, ConcurrentKeys<int>> channelServersMap = new();
    //<------------------------------->

    // client - [server]
    // used for: dispatching messages to particular client. A client will always be assigned to one server.
    private readonly ConcurrentDictionary<int, int> clientServerMap = new();

    // server - [channels]
    // used for: re-balancing when a particulate server becomes offline
    private readonly ConcurrentDictionary<int, ConcurrentKeys<int>> serverChannelsMap = new();

    // server - [client]
    // used for: health purposes and re-balancing. to know how many clients are assigned to a particular server.
    private readonly ConcurrentDictionary<int, ConcurrentKeys<int>> serverClientsMap = new();

    public int GetAvailableServerId()
    {
        if (serverClientsMap.IsEmpty)
        {
            throw WbsktExceptions.SocketServerUnavailable();
        }

        var serverId = serverClientsMap.MinBy(m => m.Value.GetKeys().Count).Key;
        logger.LogDebug("available serverId: {serverId}", serverId);
        return serverId;
    }

    public void InitializeRelations()
    {
        var servers = serverInfoProvider.GetAllSocketServerInfo();
        var channels = channelsReader.GetAll();

        // todo: channels are mapped in db but not fetched
        var clients = clientsReader.GetAll();
        MapAllChannelClients(channels, clients);
        MapAllClientServers(clients, servers);
        MapAllServerChannels(servers);
    }

    public void AssignClientToServer(int clientId, int serverId)
    {
        clientServerMap[clientId] = serverId;

        // updating server client map since it's useful to have a list/number of clients per server. `GetAvailableServerId()`

        // if server already contains client ... just return
        if (serverClientsMap[serverId].Contains(clientId))
        {
            return;
        }

        foreach (var serverClients in serverClientsMap)
        {
            serverClients.Value.Remove(clientId);
        }

        // if server already contains client ... just return
        serverClientsMap.AddOrUpdate(
            serverId,
            new ConcurrentKeys<int>([clientId]),
            (_, clients) =>
            {
                clients.Add(clientId);
                return clients;
            }
        );
    }

    public void SetClientChannels(int clientId, int[] channelIds)
    {
        var serverId = clientServerMap[clientId];
        foreach (var channelId in channelIds)
        {
            channelClientsMap[channelId].Add(clientId);
            channelServersMap.AddOrUpdate(channelId, new ConcurrentKeys<int>([serverId]), (_, serverIds) =>
            {
                serverIds.Add(serverId);
                return serverIds;
            });
        }
    }

    public void RemoveServerMappings(int serverId)
    {
        serverClientsMap.Remove(serverId, out var clientIds);
        serverChannelsMap.Remove(serverId, out var channelIds);

        foreach (var cliId in clientIds?.GetKeys() ?? Array.Empty<int>())
        {
            clientServerMap.Remove(cliId, out _);
        }

        // todo: optimise, use dictionary maybe
        foreach (var chlId in channelIds?.GetKeys() ?? [])
        {
            if (channelServersMap.TryGetValue(chlId, out var servers))
            {
                var currentServers = servers.GetKeys();
                currentServers.Remove(serverId);
                servers.Clear();
                foreach (var server in currentServers)
                {
                    servers.Add(server);
                }
            }
        }
    }

    public int[] GetServersForChannels(int[] channelIds)
    {
        var serverIds = new List<int>();
        foreach (var channelId in channelIds)
        {
            serverIds.AddRange(channelServersMap[channelId].GetKeys());
        }

        return serverIds.ToArray();
    }

    #region Initialization

    private void MapAllServerChannels(IReadOnlyCollection<ServerInfo> servers)
    {
        serverChannelsMap.Clear();
        channelServersMap.Clear();
        foreach (var server in servers)
        {
            var channelIds = new ConcurrentKeys<int>();
            var clientsIds = serverClientsMap[server.ServerId];
            foreach (var channelId in channelClientsMap.Where(ccm => ccm.Value.GetKeys().Intersect(clientsIds.GetKeys()).Any()).Select(c => c.Key))
            {
                channelIds.Add(channelId);

                if (channelServersMap.TryGetValue(channelId, out var serverIds))
                {
                    serverIds.Add(server.ServerId);
                }
                else
                {
                    if (!channelServersMap.TryAdd(channelId, new ConcurrentKeys<int>([server.ServerId])))
                    {
                        logger.LogError("couldn't add value to 'channelServersMap'");
                    }
                }
            }

            serverChannelsMap.TryAdd(server.ServerId, channelIds);
        }
    }

    private void MapAllChannelClients(IReadOnlyCollection<ChannelReadRecord> channels, IReadOnlyCollection<ClientReadRecord> clients)
    {
        channelClientsMap.Clear();
        foreach (var channel in channels)
        {
            var clientIds = new ConcurrentKeys<int>();
            var channelClientIds = clientsChannelsReader.GetClientIdsForChannel(channel.Id);
            foreach (var clientId in channelClientIds)
            {
                clientIds.Add(clientId);
            }

            channelClientsMap.TryAdd(channel.Id, clientIds);
        }
    }

    private void MapAllClientServers(IReadOnlyCollection<ClientReadRecord> clients, IReadOnlyCollection<ServerInfo> servers)
    {
        serverClientsMap.Clear();
        clientServerMap.Clear();
        foreach (var server in servers)
        {
            var clientIds = new ConcurrentKeys<int>();
            foreach (var client in clients.Where(c => c.ServerId == server.ServerId))
            {
                clientIds.Add(client.Id);
                clientServerMap.TryAdd(client.Id, server.ServerId);
            }

            serverClientsMap.TryAdd(server.ServerId, clientIds);
        }
    }

    #endregion
}
