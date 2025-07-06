using System.Collections.Concurrent;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedClientsChannelsReader(ILogger<CachedClientsChannelsReader> logger, IClientsChannelsDatabaseReader clientsChannelsReader) : IDatabaseChangeListener, IClientsChannelsReader
{
    private static DateTime _lastModified = DateTime.MinValue;
    private readonly ConcurrentDictionary<int, List<ClientChannelReadRecord>> clientToChannels = [];
    private readonly ConcurrentDictionary<int, List<ClientChannelReadRecord>> channelToClients = [];

    public IReadOnlyCollection<int> GetChannelIdsForClient(int clientId)
    {
        RefreshCacheIfEmpty();
        if (clientToChannels.TryGetValue(clientId, out var channels))
        {
            return [.. channels.Where(cc => !cc.Deleted).Select(cc => cc.ChannelId)];
        }
        return [];
    }

    public IReadOnlyCollection<int> GetClientIdsForChannel(int channelId)
    {
        RefreshCacheIfEmpty();
        if (channelToClients.TryGetValue(channelId, out var clients))
        {
            return [.. clients.Where(cc => !cc.Deleted).Select(cc => cc.ClientId)];
        }
        return [];
    }

    public IReadOnlyCollection<int> GetChannelIdsForClients(int[] clientIds)
    {
        RefreshCacheIfEmpty();
        var allChannelIds = new HashSet<int>();

        foreach (var clientId in clientIds)
        {
            if (clientToChannels.TryGetValue(clientId, out var channels))
            {
                foreach (var channel in channels.Where(cc => !cc.Deleted))
                {
                    allChannelIds.Add(channel.ChannelId);
                }
            }
        }

        return allChannelIds.ToList().AsReadOnly();
    }

    public IReadOnlyCollection<int> GetClientIdsForChannels(int[] channelIds)
    {
        RefreshCacheIfEmpty();
        var allClientIds = new HashSet<int>();

        foreach (var channelId in channelIds)
        {
            if (channelToClients.TryGetValue(channelId, out var clients))
            {
                foreach (var client in clients.Where(cc => !cc.Deleted))
                {
                    allClientIds.Add(client.ClientId);
                }
            }
        }

        return allClientIds.ToList().AsReadOnly();
    }

    public void RegisterDatabaseListener()
    {
        if (clientsChannelsReader is ClientsChannelsDatabaseReader clientsChannelsReaderImp)
        {
            clientsChannelsReaderImp.RegisterSqlDependency(OnDatabaseChange);
        }
    }

    private void OnDatabaseChange(object sender, SqlNotificationEventArgs e)
    {
        logger.LogInformation("database change detected: {Info}", e.Info);

        RefreshCache();
        RegisterDatabaseListener(); // re-register after change
    }

    private void RefreshCacheIfEmpty()
    {
        if (clientToChannels.IsEmpty || channelToClients.IsEmpty)
        {
            RefreshCache();
        }
    }

    private void RefreshCache()
    {
        try
        {
            var clientChannels = clientsChannelsReader.GetAll(_lastModified);
            var maxLastModified = _lastModified;

            foreach (var clientChannel in clientChannels)
            {
                // Update client to channels mapping
                clientToChannels.AddOrUpdate(
                    clientChannel.ClientId,
                    [clientChannel],
                    (_, channels) =>
                    {
                        var existing = channels.FirstOrDefault(cc => cc.ChannelId == clientChannel.ChannelId);
                        if (existing != null)
                        {
                            channels.Remove(existing);
                        }
                        channels.Add(clientChannel);
                        return channels;
                    });

                // Update channel to clients mapping
                channelToClients.AddOrUpdate(
                    clientChannel.ChannelId,
                    [clientChannel],
                    (_, clients) =>
                    {
                        var existing = clients.FirstOrDefault(cc => cc.ClientId == clientChannel.ClientId);
                        if (existing != null)
                        {
                            clients.Remove(existing);
                        }
                        clients.Add(clientChannel);
                        return clients;
                    });

                if (clientChannel.LastModified > maxLastModified)
                {
                    maxLastModified = clientChannel.LastModified;
                }
            }

            _lastModified = maxLastModified;
            logger.LogDebug("cache refreshed with {count} client-channel relations", clientChannels.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to refresh clients-channels cache");
            throw;
        }
    }
} 