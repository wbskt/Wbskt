using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Wbskt.Common.Providers;
using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services.Implementations;

public class PayloadDispatcher(
    ILogger<ServerInfoService> logger,
    ICachedServerInfoProvider serverInfoProvider,
    IPublishersChannelsService publishersChannelsService,
    IAuthService authService,
    IRelationService relationService) : IPayloadDispatcher
{
    public async Task<bool> DispatchPayload(ClientPayload payload)
    {
        var publisherId = payload.PublisherRef;

        var channelIds = publishersChannelsService.GetChannelsForPublisherRef(publisherId).Select(c => c.Id).ToArray();

        var tasks = new List<Task>();

        if (channelIds.Length == 0)
        {
            logger.LogWarning("there are no channels with publisherId: {publisherId}", publisherId);
            return false;
        }

        foreach (var serverId in relationService.GetServersForChannels(channelIds))
        {
            logger.LogDebug("dispatcher task queued for socket server: {serverId}, publisherId: {publisherId}", serverId, payload);
            tasks.Add(DispatchPayloadToServer(serverId, payload));
        }

        await Task.WhenAll(tasks);
        return true;
    }

    private async Task DispatchPayloadToServer(int serverId, ClientPayload payload)
    {
        var token = authService.CreateCoreServerToken();
        var authHeader = new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, token);
        var server = serverInfoProvider.GetById(serverId);

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{server.GetAddressWithFallback()}"),
            DefaultRequestHeaders = { Authorization = authHeader }
        };

        try
        {
            logger.LogDebug("post: {url}/dispatch with publisher id: {publisherId}", httpClient.BaseAddress, payload.PublisherRef);
            var result = await httpClient.PostAsync("dispatch", JsonContent.Create(payload));
            if (!result.IsSuccessStatusCode)
            {
                logger.LogError("dispatch to {server} Id:({serverId}) failed with: {reason}", server.GetAddressWithFallback(), server.ServerId, result.ReasonPhrase);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("error while request to {url}/dispatch, error: {details}", httpClient.BaseAddress, ex.Message);
        }
    }
}
