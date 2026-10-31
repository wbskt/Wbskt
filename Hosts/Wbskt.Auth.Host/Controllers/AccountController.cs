using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>The signed-in person's own account, as opposed to signing in (<see cref="AuthController"/>).</summary>
[Route("api/auth/account")]
[ApiController]
public class AccountController : ApiControllerBase
{
    private readonly IAccountDeletionService _deletion;
    private readonly RefreshTokenCookie _refreshCookie;

    public AccountController(IAccountDeletionService deletion, RefreshTokenCookie refreshCookie)
    {
        _deletion = deletion;
        _refreshCookie = refreshCookie;
    }

    /// <summary>
    /// Deletes the signed-in account, after the password is entered again.
    /// </summary>
    /// <remarks>
    /// Tenants only this person belongs to are deleted with their workspaces, and everything in them;
    /// they leave every other tenant, and the workspaces they own there pass to another administrator.
    /// While they are the only administrator of a tenant others belong to, the whole deletion is
    /// refused with 409 <c>ACCOUNT_SOLE_ADMINISTRATOR</c>, naming those tenants. Every session ends,
    /// on every host, and the account's username and address are anonymised, so the address can
    /// register again. A wrong password is 400 <c>AUTH_CURRENT_PASSWORD_INVALID</c> and counts towards
    /// the account lockout. POST rather than DELETE, so the password travels in a body every proxy
    /// keeps.
    /// </remarks>
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HashesPassword]
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _deletion.DeleteAccountAsync(userIdResult.Value, request.Password, CallerIpAddress(), cancellationToken);
        if (result.IsSuccess && RefreshTokenCookie.IsRequested(Request))
        {
            _refreshCookie.Clear(Response);
        }

        return MapResult(result);
    }

    private string CallerIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
