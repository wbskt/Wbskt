using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common;
using Wbskt.Common.Contracts;
using Wbskt.Common.Extensions;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelsController(ILogger<ChannelsController> logger, IChannelsService channelsService, IClientService clientService, IServerInfoService serverInfoService) : ControllerBase
{
    [HttpGet]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult GetAll()
    {
        var userId = User.GetUserId();
        IEnumerable<ChannelDetails> details = channelsService.GetChannelsForUser(userId);
        return Ok(details);
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult CreateChannel(ChannelCreationRequest channelCreation)
    {
        channelCreation.UserId = User.GetUserId();
        ChannelDetails details = channelsService.CreateChannel(channelCreation);
        return Ok(details);
    }

    [HttpPost("client")]
    [AllowAnonymous]
    public IActionResult SubscribeToChannel(ClientConnectionRequest request)
    {
        if (!channelsService.VerifyChannel(request.Channels))
        {
            logger.LogWarning("channel secrets does not match the subscriptionIds");
            return Unauthorized();
        }

        string clientToken = clientService.AddClientConnection(request);
        return Ok(clientToken);
    }

    [HttpGet("{publisherId:guid}/dispatch")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public async Task<IActionResult> Dispatch(Guid publisherId)
    {
        var payload = new ClientPayload();
        payload.PublisherId = publisherId;
        return await Dispatch(payload);
    }

    [HttpPost("/dispatch")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public async Task<IActionResult> Dispatch(ClientPayload payload)
    {
        payload.PayloadId = Guid.NewGuid();
        var payloadSend = await serverInfoService.DispatchPayload(payload);
        return payloadSend ? Ok() : BadRequest($"no channels with publisherId: {payload.PublisherId}");
    }
}
