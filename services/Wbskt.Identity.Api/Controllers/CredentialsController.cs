using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Readers;
using Wbskt.Common.Services;
using Wbskt.Identity.Api.Contracts;

namespace Wbskt.Identity.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class CredentialsController : ControllerBase
{
    private readonly ICredentialService _credentialService;
    private readonly ICredentialsReader _credentialsReader;
    private readonly ICurrentUser _currentUser;

    public CredentialsController(ICredentialService credentialService, ICredentialsReader credentialsReader, ICurrentUser currentUser)
    {
        _credentialService = credentialService;
        _credentialsReader = credentialsReader;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetCredentials(CancellationToken cancellationToken)
    {
        var credentials = await _credentialsReader.GetAllForUserAsync(_currentUser.Id, cancellationToken);
        return Ok(credentials);
    }

    [HttpPost]
    public async Task<IActionResult> SaveCredentials([FromBody] SaveCredentialsRequest request, CancellationToken cancellationToken)
    {
        await _credentialService.SaveCredentialsAsync(_currentUser.Id, request.IntegrationType, request.Name, request.Credentials, cancellationToken);
        return NoContent();
    }
}
