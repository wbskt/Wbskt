using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common;
using Wbskt.Common.Extensions;
using Wbskt.Socket.Api.Services;

namespace Wbskt.Socket.Api.Controllers;

[ApiController]
[Route("/ws")]
public class ConnectionController : ControllerBase
{
    private readonly IClientConnectionManager _connectionManager;

    public ConnectionController(IClientConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = Constants.AuthSchemes.ClientScheme)]
    public async Task Get()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var clientId = HttpContext.User.GetClientId();
            await _connectionManager.OnConnected(clientId, webSocket);
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
}
