using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PublishersController(ILogger<PublishersController> logger, IPublisherService publisherService, IPublishersChannelsService publishersChannelsService) : ControllerBase
{
    [HttpGet]
    public IReadOnlyCollection<PublisherRecord> GetAll()
    {
        var userId = User.GetUserId();
        var details = publisherService.GetAllForUser(userId);
        logger.LogDebug("retrieved {count} channels", details.Count);
        return details;
    }

    [HttpPost]
    public PublisherRecord CreatePublisher(PublisherRecord publisherRecord)
    {
        publisherRecord.UserId = User.GetUserId();
        publisherService.Create(publisherRecord);
        return publisherRecord;
    }

    [HttpGet("{publisherId:int}/channels")]
    public IActionResult GetChannelsForPublisher(int publisherId)
    {
        var channelIds = publishersChannelsService.GetChannelIdsForPublisher(publisherId);
        logger.LogDebug("retrieved {count} channels for publisher {publisherId}", channelIds.Count, publisherId);
        return Ok(channelIds);
    }

    [HttpPost("{publisherId:int}/channels")]
    public IActionResult AddChannelsToPublisher(int publisherId, [FromBody] int[] channelIds)
    {
        foreach (var channelId in channelIds)
        {
            publishersChannelsService.AddPublisherToChannel(publisherId, channelId);
        }
        logger.LogDebug("added {count} channels to publisher {publisherId}", channelIds.Length, publisherId);
        return Ok();
    }

    [HttpDelete("{publisherId:int}/channels")]
    public IActionResult RemoveChannelsFromPublisher(int publisherId, [FromBody] int[] channelIds)
    {
        foreach (var channelId in channelIds)
        {
            publishersChannelsService.RemovePublisherFromChannel(publisherId, channelId);
        }
        logger.LogDebug("removed {count} channels from publisher {publisherId}", channelIds.Length, publisherId);
        return Ok();
    }

    [HttpPost("channels")]
    public IActionResult GetChannelsForPublishers([FromBody] int[] publisherIds)
    {
        var channelIds = publishersChannelsService.GetChannelIdsForPublishers(publisherIds);
        logger.LogDebug("retrieved {count} unique channels for {publisherCount} publishers", channelIds.Count, publisherIds.Length);
        return Ok(channelIds);
    }
}
