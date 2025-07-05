using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PublishersController(ILogger<PublishersController> logger, IPublisherService publisherService) : ControllerBase
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

    [HttpPut("{publisherId:guid}")]
    public PublisherRecord UpdatePublisher(Guid publisherId)
    {
        return Ok();
    }
}
