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
public class ChannelsController(ILogger<ChannelsController> logger, IChannelsService channelsService, IClientService clientService) : ControllerBase
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
}
