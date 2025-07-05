using Microsoft.AspNetCore.Mvc;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PublishersController : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok();
    }

    [HttpPost]
    public IActionResult CreatePublisher()
    {
        return Ok();
    }

    [HttpPut("{publisherId:guid}")]
    public IActionResult UpdatePublisher(Guid publisherId)
    {
        return Ok();
    }
}
