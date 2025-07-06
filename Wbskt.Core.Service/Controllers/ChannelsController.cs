using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelsController(ILogger<ChannelsController> logger, IChannelsService channelsService, IPublishersChannelsService publishersChannelsService) : ControllerBase
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

    [HttpGet("{channelId:int}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult GetPublishersForChannel(int channelId)
    {
        var publisherIds = publishersChannelsService.GetPublisherIdsForChannel(channelId);
        logger.LogDebug("retrieved {count} publishers for channel {channelId}", publisherIds.Count, channelId);
        return Ok(publisherIds);
    }

    [HttpPost("{channelId:int}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult AddPublishersToChannel(int channelId, [FromBody] int[] publisherIds)
    {
        foreach (var publisherId in publisherIds)
        {
            publishersChannelsService.AddPublisherToChannel(publisherId, channelId);
        }
        logger.LogDebug("added {count} publishers to channel {channelId}", publisherIds.Length, channelId);
        return Ok();
    }

    [HttpDelete("{channelId:int}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult RemovePublishersFromChannel(int channelId, [FromBody] int[] publisherIds)
    {
        foreach (var publisherId in publisherIds)
        {
            publishersChannelsService.RemovePublisherFromChannel(publisherId, channelId);
        }
        logger.LogDebug("removed {count} publishers from channel {channelId}", publisherIds.Length, channelId);
        return Ok();
    }

    [HttpPost("publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult GetPublishersForChannels([FromBody] int[] channelIds)
    {
        var publisherIds = publishersChannelsService.GetPublisherIdsForChannels(channelIds);
        logger.LogDebug("retrieved {count} unique publishers for {channelCount} channels", publisherIds.Count, channelIds.Length);
        return Ok(publisherIds);
    }
}
