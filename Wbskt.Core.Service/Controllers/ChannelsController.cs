using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelsController(ILogger<ChannelsController> logger, IChannelsService channelsService) : ControllerBase
{
    [HttpGet]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IReadOnlyCollection<ChannelRecord> GetAll()
    {
        var userId = User.GetUserId();
        var details = channelsService.GetAllForUser(userId);
        logger.LogDebug("retrieved {count} channels", details.Count);
        return details;
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public ChannelRecord CreateChannel(ChannelRecord channelRecord)
    {
        channelRecord.UserId = User.GetUserId();
        channelsService.Create(channelRecord);
        return channelRecord;
    }
}
