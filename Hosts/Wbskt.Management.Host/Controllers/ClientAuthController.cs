using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Controllers;

[Route("api/client-auth")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public class ClientAuthController : ControllerBase
{
    private readonly IClientAuthService _authService;
    private readonly ILogger<ClientAuthController> _logger;

    public ClientAuthController(IClientAuthService authService, ILogger<ClientAuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<ActionResult<ClientLoginResponse>> Login(ClientLoginRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Client Login requested for ClientRefId: {ClientRefId}", request.ClientRefId);
        var result = await _authService.LoginAsync(request, cancellationToken);
        return MapResult(result);
    }

    private IActionResult MapResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return MapError(result.Error);
    }

    private ActionResult<T> MapResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapError(result.Error);
    }

    private ActionResult MapError(Error error)
    {
        _logger.LogWarning("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);
        return error.Type switch
        {
            ErrorType.Validation => BadRequest(error),
            ErrorType.NotFound => NotFound(error),
            ErrorType.Conflict => Conflict(error),
            ErrorType.Unauthorized => Unauthorized(error),
            _ => BadRequest(error)
        };
    }
}
