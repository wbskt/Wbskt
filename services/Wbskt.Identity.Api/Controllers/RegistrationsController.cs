using Microsoft.AspNetCore.Mvc;
using Wbskt.Identity.Api.Contracts;
using Wbskt.Identity.Api.Services;

namespace Wbskt.Identity.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class RegistrationsController : ControllerBase
{
    private readonly IRegistrationService _registrationService;

    public RegistrationsController(IRegistrationService registrationService)
    {
        _registrationService = registrationService;
    }

    [HttpPost]
    public async Task<IActionResult> RegisterClient(ClientRegistrationRequest request, CancellationToken cancellationToken)
    {
        var response = await _registrationService.RegisterClientAsync(request, cancellationToken);
        return Ok(response);
    }
}
