using Microsoft.AspNetCore.Mvc;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ClientsController(Logger<ClientsController> logger, IClientService clientService) : ControllerBase
{
    [HttpGet]
    public IActionResult GetClients()
    {
        return Ok();
    }

    [HttpPost("{eCode:alpha}/enroll")]
    public IActionResult Enroll(string eCode)
    {
        return Ok();
    }

    [HttpPost("connect")]
    public IActionResult RequestConnection()
    {
        return Ok();
    }
}
