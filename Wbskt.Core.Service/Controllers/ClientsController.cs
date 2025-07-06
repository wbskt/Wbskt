using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Extensions;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ClientsController(Logger<ClientsController> logger, IClientsManagementService clientsService) : ControllerBase
{
    [HttpGet]
    public IActionResult GetClients()
    {
        logger.LogTrace("getting all clients");
        var userId = User.GetUserId();
        var clients = clientsService.GetAllByUserId(userId);
        return Ok(clients);
    }

    [HttpPost("{eCode:alpha}/enroll")]
    public IActionResult Enroll(string eCode)
    {
        logger.LogTrace("enrollment endpoint called with code {eCode}", eCode);
        return Ok();
    }

    [HttpPost("connect")]
    public IActionResult RequestConnection()
    {
        logger.LogTrace("connection request endpoint called");
        return Ok();
    }
}
