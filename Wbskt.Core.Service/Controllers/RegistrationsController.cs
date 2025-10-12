using Microsoft.AspNetCore.Mvc;
using Wbskt.Core.Service.Contracts;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Controllers;

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
