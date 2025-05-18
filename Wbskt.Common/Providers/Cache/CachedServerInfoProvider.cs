using Microsoft.Extensions.Logging;
using Wbskt.Common.Contracts;
using Wbskt.Common.Exceptions;

namespace Wbskt.Common.Providers.Cache;

internal sealed class CachedServerInfoProvider(ILogger<CachedServerInfoProvider> logger, IServerInfoProvider serverInfoProvider) : ICachedServerInfoProvider
{
    private static readonly string ServerType = Environment.GetEnvironmentVariable(nameof(ServerType)) ?? Constants.ServerType.CoreServer.ToString();

    private readonly List<ServerInfo> serverInfos = [];
    private readonly object @lock = new();

    public IReadOnlyCollection<ServerInfo> GetAll()
    {
        lock (@lock)
        {
            if (serverInfos.Count != 0)
            {
                return [.. serverInfos]; // return a copy to prevent external mutation
            }

            var records = serverInfoProvider.GetAll();
            serverInfos.AddRange(records);

            return [.. serverInfos]; // return a copy to prevent external mutation
        }
    }

    public int Insert(ServerInfo serverInfo)
    {
        var serverId = serverInfoProvider.Insert(serverInfo);
        RefreshCache();
        return serverId;
    }

    public void UpdatePublicDomainName(int id, string publicDomainName)
    {
        if (id <= 0)
        {
            logger.LogError("invalid id provided: {id}", id);
            throw WbsktExceptions.InvalidId(id, "Server");
        }

        if (ServerType == Constants.ServerType.CoreServer.ToString())
        {
            logger.LogError("core server cannot perform this operation: {operationName}", nameof(UpdatePublicDomainName));
            return;
        }

        if (GetAll().Any(s => s.ServerId == id) == false)
        {
            logger.LogError("server with id: {serverId} does not exists", id);
            throw WbsktExceptions.UnknownSocketServer(id);
        }

        serverInfoProvider.UpdatePublicDomainName(id, publicDomainName);
        lock (@lock)
        {
            var info = serverInfos.FirstOrDefault(s => s.ServerId == id);
            if (info != null)
            {
                info.PublicDomainName = publicDomainName;
            }
        }
    }

    public void UpdateServerStatus(int id, bool active)
    {
        if (id <= 0)
        {
            logger.LogError("invalid id provided: {id}", id);
            throw WbsktExceptions.InvalidId(id, "Server");
        }

        if (GetAll().Any(s => s.ServerId == id) == false)
        {
            RefreshCache();
            if (GetAll().Any(s => s.ServerId == id) == false)
            {
                logger.LogError("server with id: {serverId} does not exists", id);
                throw WbsktExceptions.UnknownSocketServer(id);
            }
        }

        serverInfoProvider.UpdateServerStatus(id, active);
        lock (@lock)
        {
            var info = serverInfos.FirstOrDefault(s => s.ServerId == id);
            if (info != null)
            {
                info.Active = active;
            }
        }
    }

    public IReadOnlyCollection<ServerInfo> GetAllSocketServerInfo()
    {
        var servers = GetAll();
        return [..servers.Where(s => s.Type == Constants.ServerType.SocketServer)];
    }

    public IReadOnlyCollection<ServerInfo> GetAllCoreServerInfo()
    {
        var servers = GetAll();
        return [..servers.Where(s => s.Type == Constants.ServerType.CoreServer)];
    }

    public ServerInfo GetById(int serverId)
    {
        if (serverId <= 0)
        {
            throw WbsktExceptions.InvalidId(serverId, "Server");
        }

        var servers = GetAll();
        var server = servers.FirstOrDefault(s => s.ServerId == serverId);
        if (server == null)
        {
            throw WbsktExceptions.UnknownSocketServer(serverId);
        }

        return server;
    }

    private void RefreshCache()
    {
        var records = serverInfoProvider.GetAll();
        lock (@lock)
        {
            serverInfos.Clear();
            serverInfos.AddRange(records);
        }
    }
}
