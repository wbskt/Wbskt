using Wbskt.Common.Providers;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class ServerInfoService(ILogger<ServerInfoService> logger, ICachedServerInfoProvider serverInfoProvider, IRelationService relationService) : IServerInfoService
{
    public void UpdateServerStatus(int serverId, bool active)
    {
        logger.LogDebug("updating status of server: {serverId} - {active}", serverId, active);
        serverInfoProvider.UpdateServerStatus(serverId, active);
        if (active == false)
        {
            relationService.RemoveServerMappings(serverId);
        }
    }
}
