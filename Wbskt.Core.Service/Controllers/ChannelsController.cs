using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelsController(ILogger<ChannelsController> logger, IChannelsService channelsService, IPublishersChannelsService publishersChannelsService, IPublishersService publishersService) : ControllerBase
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

    [HttpGet("{subscriptionRef:guid}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult GetPublishersForChannel(Guid subscriptionRef)
    {
        var channelId = channelsService.GetChannelIdBySubscriptionRef(subscriptionRef);
        var publisherIds = publishersChannelsService.GetPublisherIdsForChannel(channelId);
        var publisherRefs = publishersService.GetPublisherRefsByIds(publisherIds.ToArray());
        logger.LogDebug("retrieved {count} publishers for channel {subscriptionRef}", publisherRefs.Count, subscriptionRef);
        return Ok(publisherRefs);
    }

    [HttpPost("{subscriptionRef:guid}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult AddPublishersToChannel(Guid subscriptionRef, [FromBody] Guid[] publisherRefs)
    {
        var channelId = channelsService.GetChannelIdBySubscriptionRef(subscriptionRef);
        var publisherIds = publishersService.GetPublisherIdsByRefs(publisherRefs);
        publishersChannelsService.AddPublishersToChannel(publisherIds.ToArray(), channelId);
        logger.LogDebug("added {count} publishers to channel {subscriptionRef}", publisherRefs.Length, subscriptionRef);
        return Ok();
    }

    [HttpDelete("{subscriptionRef:guid}/publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult RemovePublishersFromChannel(Guid subscriptionRef, [FromBody] Guid[] publisherRefs)
    {
        var channelId = channelsService.GetChannelIdBySubscriptionRef(subscriptionRef);
        var publisherIds = publishersService.GetPublisherIdsByRefs(publisherRefs);
        publishersChannelsService.RemovePublishersFromChannel(publisherIds.ToArray(), channelId);
        logger.LogDebug("removed {count} publishers from channel {subscriptionRef}", publisherRefs.Length, subscriptionRef);
        return Ok();
    }

    [HttpPost("publishers")]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.UserScheme)]
    public IActionResult GetPublishersForChannels([FromBody] Guid[] subscriptionRefs)
    {
        var channelIds = channelsService.GetChannelIdsBySubscriptionRefs(subscriptionRefs);
        var publisherIds = publishersChannelsService.GetPublisherIdsForChannels(channelIds.ToArray());
        var publisherRefs = publishersService.GetPublisherRefsByIds(publisherIds.ToArray());
        logger.LogDebug("retrieved {count} unique publishers for {channelCount} channels", publisherRefs.Count, subscriptionRefs.Length);
        return Ok(publisherRefs);
    }
}
