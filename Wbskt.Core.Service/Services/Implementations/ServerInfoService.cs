using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Wbskt.Common.Contracts;
using Wbskt.Common.Providers;

namespace Wbskt.Core.Service.Services.Implementations;

public class ServerInfoService(ILogger<ServerInfoService> logger, ICachedServerInfoProvider serverInfoProvider, ICachedChannelsProvider channelsService, IAuthService authService, IRelationService relationService) : IServerInfoService
{
    public async Task<bool> DispatchPayload(ClientPayload payload)
    {
        var publisherId = payload.PublisherId;

        var channelIds = channelsService.GetAllByChannelPublisherId(publisherId).Select(c => c.ChannelId).ToArray();

        var tasks = new List<Task>();

        if (channelIds.Length == 0)
        {
            logger.LogWarning("there are no channels with publisherId: {publisherId}", publisherId);
            return false;
        }

        foreach (var serverId in relationService.GetServersForChannels(channelIds))
        {
            logger.LogDebug("Dispatcher task queued for socket server: {serverId}, publisherId: {publisherId}", serverId, payload);
            tasks.Add(DispatchPayloadToServer(serverId, payload));
        }
        await Task.WhenAll(tasks);
        return true;

    }

    public void UpdateServerStatus(int serverId, bool active)
    {
        logger.LogDebug("updating status of server: {serverId} - {active}", serverId, active);
        serverInfoProvider.UpdateServerStatus(serverId, active);
        if (active == false)
        {
            relationService.RemoveServerMappings(serverId);
        }
    }

    private async Task DispatchPayloadToServer(int serverId, ClientPayload payload)
    {
        var token = authService.CreateCoreServerToken();
        var authHeader = new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, token);
        var server = serverInfoProvider.GetById(serverId);

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{server.GetAddressWithFallback()}"),
            DefaultRequestHeaders = { Authorization = authHeader}
        };

        try
        {
            logger.LogDebug("post: {url}/dispatch with publisher id: {publisherId}", httpClient.BaseAddress, payload.PublisherId);
            var result = await httpClient.PostAsync($"dispatch", JsonContent.Create(payload));
            if (!result.IsSuccessStatusCode)
            {
                logger.LogError("dispatch to {server} Id:({serverId}) failed with: {reason}", server.GetAddressWithFallback(), server.ServerId, result.ReasonPhrase);
            }
        }
        catch(Exception ex)
        {
            logger.LogWarning("error while request to {url}/dispatch/{publisher}, error: {details}", httpClient.BaseAddress, payload.PublisherId, ex.Message);
        }
    }
}
