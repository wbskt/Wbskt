using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Controllers;

[Route("api/client-registrations")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public class ClientRegistrationsController : ControllerBase
{
    private readonly IClientRegistrationService _registrationService;
    private readonly ILogger<ClientRegistrationsController> _logger;

    public ClientRegistrationsController(IClientRegistrationService registrationService, ILogger<ClientRegistrationsController> logger)
    {
        _registrationService = registrationService;
        _logger = logger;
    }

    [HttpPost("initiate")]
    public async Task<ActionResult<ClientRegistrationResponse>> Initiate(ClientRegistrationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Client Registration initiated for Client Name: '{ClientName}'", request.Name);
        var result = await _registrationService.InitiateRegistrationAsync(request, cancellationToken);
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
