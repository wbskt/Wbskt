using Microsoft.AspNetCore.Mvc;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ClientsController : ControllerBase
{
    [HttpGet]
    public IActionResult GetClients()
    {
        return Ok();
    }

    [HttpPost("{eCode:alpha}/enroll")]
    public IActionResult Enroll(int eCode)
    {
        return Ok();
    }

    [HttpPost("connect")]
    public IActionResult RequestConnection()
    {
        return Ok();
    }
}
