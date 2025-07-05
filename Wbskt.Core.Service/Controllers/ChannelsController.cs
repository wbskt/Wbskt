using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common;
using Wbskt.Common.Contracts;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelsController(ILogger<ChannelsController> logger, IChannelsService channelsService, IClientService clientService, IPayloadDispatcher payloadDispatcher) : ControllerBase
{
    [HttpGet]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult GetAll()
    {
        var userId = User.GetUserId();
        var details = channelsService.GetChannelsForUser(userId);
        return Ok(details);
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult CreateChannel(ChannelRecord channelRecord)
    {
        channelRecord.UserId = User.GetUserId();
        channelsService.CreateChannel(channelRecord);
        return Ok(channelRecord);
    }

    [HttpPost("client")]
    [AllowAnonymous]
    public IActionResult SubscribeToChannel(ClientConnectionRequest request)
    {
        // if (!channelsService.VerifyChannel(request.Channels))
        // {
        //     logger.LogWarning("channel secrets does not match the subscriptionIds");
        //     return Unauthorized();
        // }

        var clientToken = clientService.AddClientConnection(request);
        return Ok(clientToken);
    }

    [HttpGet("{publisherId:guid}/dispatch")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public async Task<IActionResult> Dispatch(Guid publisherId)
    {
        var payload = new ClientPayload
        {
            PublisherId = publisherId
        };

        return await Dispatch(payload);
    }

    [HttpPost("/dispatch")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public async Task<IActionResult> Dispatch(ClientPayload payload)
    {
        return await DispatchInternal(payload) ? Ok() : BadRequest($"no channels with publisherId: {payload.PublisherId}");
    }

    private async Task<bool> DispatchInternal(ClientPayload payload)
    {
        payload.PayloadId = Guid.NewGuid();
        var payloadSend = await payloadDispatcher.DispatchPayload(payload);
        return payloadSend;
    }
}
