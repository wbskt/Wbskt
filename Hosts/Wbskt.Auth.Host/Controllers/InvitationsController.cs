using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Redeeming an invitation. Separate from <see cref="ManagementController"/> because that controller
/// is rooted at <c>api/tenants/{tenantRef}</c> and everything under it resolves the tenant from the
/// route — which the invitee cannot supply. Discovering which tenant the token belongs to is the
/// call's purpose, so it cannot also be its precondition.
/// </summary>
[Route("api/invitations")]
[ApiController]
[Authorize]
public class InvitationsController : ApiControllerBase
{
    private readonly IManagementService _managementService;
    private readonly ILogger<InvitationsController> _logger;

    public InvitationsController(IManagementService managementService, ILogger<InvitationsController> logger)
    {
        _managementService = managementService;
        _logger = logger;
    }

    /// <summary>
    /// Joins the calling account to the tenant that issued the token, granting the invited role if
    /// one was named. The account's email must match the address the invitation was sent to.
    /// <para>
    /// A user who does not have an account yet passes the same token to
    /// <c>POST /api/auth/register</c> instead, which registers and redeems in one step.
    /// </para>
    /// </summary>
    [HttpPost("accept")]
    public async Task<ActionResult<AcceptInvitationResponse>> Accept([FromBody] AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        // The token is a bearer credential for tenant membership and is deliberately absent here.
        _logger.LogInformation("API: AcceptInvitation requested");

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.AcceptInvitationAsync(caller.Value, request.Token, cancellationToken));
    }
}
