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
    public IEnumerable<ChannelRecord> GetAll()
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

    [HttpGet("{channelRef:guid}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IEnumerable<PublisherRecord> GetPublishersForChannel(Guid channelRef)
    {
        var publishers = publishersChannelsService.GetPublishersForChannelRef(channelRef);
        logger.LogDebug("retrieved {count} publishers for channel {channelRef}", publishers.Count, channelRef);
        return publishers;
    }

    [HttpPost("{channelRef:guid}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult AddPublishersToChannel(Guid channelRef, [FromBody] Guid[] publisherRefs)
    {
        var success = publishersChannelsService.AddPublishersToChannel(publisherRefs, channelRef);
        if (success)
        {
            logger.LogDebug("added {count} publishers to channel {channelRef}", publisherRefs.Length, channelRef);
            return Ok();
        }

        return BadRequest("could not add publishers to the given channel");
    }

    [HttpDelete("{channelRef:guid}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult RemovePublishersFromChannel(Guid channelRef, [FromBody] Guid[] publisherRefs)
    {
        var success = publishersChannelsService.RemovePublishersFromChannel(publisherRefs, channelRef);

        if (success)
        {
            logger.LogDebug("removed {count} publishers from channel {channelRef}", publisherRefs.Length, channelRef);
            return Ok();
        }

        return BadRequest("could not remove publishers from channel");
    }

    [HttpPost("publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IEnumerable<ChannelPublishers> GetPublishersForChannels([FromBody] Guid[] channelRefs)
    {
        var publishers = publishersChannelsService.GetPublishersForChannels(channelRefs);
        return publishers;
    }
}
