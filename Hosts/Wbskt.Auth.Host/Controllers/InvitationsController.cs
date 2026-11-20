using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Auth.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Redeeming an invitation. Separate from <see cref="ManagementController"/> because that controller
/// is rooted at <c>api/tenants/{tenantRef}</c> and everything under it resolves the tenant from the
/// route — which the invitee cannot supply. Discovering which tenant the token belongs to is the
/// call's purpose, so it cannot also be its precondition.
/// </summary>
[Route("api/v1/invitations")]
[ApiController]
[Authorize]
public class InvitationsController : ApiControllerBase
{
    private readonly ITenantService _tenantService;

    public InvitationsController(ITenantService tenantService)
    {
        _tenantService = tenantService;
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
        var caller = CurrentUser();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _tenantService.AcceptInvitationAsync(caller.Value.UserId, caller.Value.UserRef, request.Token, cancellationToken));
    }

    /// <summary>
    /// What an invitation invites to, before signing in: the account, who sent it, the address it was sent to
    /// (masked), the workspaces it joins, and whether it is pending, expired, accepted or revoked. The token
    /// goes in the body, not the path, so it stays out of access logs. 400 <c>INVITATION_INVALID</c> for a token
    /// that matches nothing.
    /// </summary>
    [HttpPost("preview")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.InvitationPreview)]
    public async Task<ActionResult<InvitationPreviewResponse>> Preview([FromBody] InvitationPreviewRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _tenantService.PreviewInvitationAsync(request.Token, cancellationToken));
    }
}
