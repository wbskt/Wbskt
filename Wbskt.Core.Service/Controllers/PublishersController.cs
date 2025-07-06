using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PublishersController(ILogger<PublishersController> logger, IPublishersService publishersService, IPublishersChannelsService publishersChannelsService) : ControllerBase
{
    [HttpGet]
    public IReadOnlyCollection<PublisherRecord> GetAll()
    {
        var userId = User.GetUserId();
        var details = publishersService.GetAllForUser(userId);
        logger.LogDebug("retrieved {count} publishers", details.Count);
        return details;
    }

    [HttpPost]
    public PublisherRecord CreatePublisher(PublisherRecord publisherRecord)
    {
        publisherRecord.UserId = User.GetUserId();
        publishersService.Create(publisherRecord);
        return publisherRecord;
    }

    [HttpGet("{publisherRef:guid}/channels")]
    public IEnumerable<ChannelRecord> GetChannelsForPublisher(Guid publisherRef)
    {
        var channels = publishersChannelsService.GetChannelsForPublisherRef(publisherRef);
        logger.LogDebug("retrieved {count} channels for publisher {publisherRef}", channels.Count, publisherRef);
        return channels;
    }

    [HttpPost("{publisherRef:guid}/channels")]
    public IActionResult AddChannelsToPublisher(Guid publisherRef, [FromBody] Guid[] channelRefs)
    {
        publishersChannelsService.AddChannelsToPublisher(channelIds.ToArray(), publisherId);
        logger.LogDebug("added {count} channels to publisher {publisherRef}", channelRefs.Length, publisherRef);
        return Ok();
    }

    [HttpDelete("{publisherRef:guid}/channels")]
    public IActionResult RemoveChannelsFromPublisher(Guid publisherRef, [FromBody] Guid[] channelRefs)
    {
        var publisherId = publishersService.GetPublisherIdByRef(publisherRef);
        var channelIds = channelsService.GetChannelIdsBySubscriptionRefs(channelRefs);
        publishersChannelsService.RemoveChannelsFromPublisher(channelIds.ToArray(), publisherId);
        logger.LogDebug("removed {count} channels from publisher {publisherRef}", channelRefs.Length, publisherRef);
        return Ok();
    }

    [HttpPost("channels")]
    public IActionResult GetChannelsForPublishers([FromBody] Guid[] publisherRefs)
    {
        var publisherIds = publishersService.GetPublisherIdsByRefs(publisherRefs);
        var channelIds = publishersChannelsService.GetChannelIdsForPublishers(publisherIds.ToArray());
        var channelRefs = channelsService.GetChannelSubscriptionRefsByIds(channelIds.ToArray());
        logger.LogDebug("retrieved {count} unique channels for {publisherCount} publishers", channelRefs.Count, publisherRefs.Length);
        return Ok(channelRefs);
    }
}
