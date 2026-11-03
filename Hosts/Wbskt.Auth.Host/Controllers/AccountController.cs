using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>The signed-in person's own account, as opposed to signing in (<see cref="AuthController"/>).</summary>
[Route("api/auth/account")]
[ApiController]
public class AccountController : ApiControllerBase
{
    private readonly IAccountDeletionService _deletion;
    private readonly IAccountProfileService _profile;
    private readonly RefreshTokenCookie _refreshCookie;

    public AccountController(IAccountDeletionService deletion, IAccountProfileService profile, RefreshTokenCookie refreshCookie)
    {
        _deletion = deletion;
        _profile = profile;
        _refreshCookie = refreshCookie;
    }

    /// <summary>
    /// Changes fields of the signed-in account (for now its username), and returns a fresh access
    /// token carrying them.
    /// </summary>
    /// <remarks>
    /// A request naming no field is 400 <c>ACCOUNT_NO_CHANGES</c>. The username is a display name (sign-in is by email): 3 to 50 characters, unique, and not a
    /// reserved name or a look-alike of one (400 <c>USERNAME_RESERVED</c>). A taken name is 409
    /// <c>USERNAME_TAKEN</c>. At most one change every 30 days: sooner is 400
    /// <c>USERNAME_CHANGED_RECENTLY</c>, whose message says when. Setting the current name again
    /// changes nothing. The response has no refresh token; the session's own keeps working.
    /// </remarks>
    [Authorize]
    [HttpPatch]
    public async Task<ActionResult<LoginResponse>> UpdateAccount(UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        if (request.Username is null)
        {
            return MapError(Error.Validation("ACCOUNT_NO_CHANGES", "Name at least one field to change."));
        }

        var result = await _profile.ChangeUsernameAsync(userIdResult.Value, WbsktClaims.SessionOf(User), request.Username, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Starts changing the signed-in person's email address: mails a confirm link to the new one.
    /// </summary>
    /// <remarks>
    /// Needs the password (400 <c>AUTH_CURRENT_PASSWORD_INVALID</c>, counted towards the lockout). The
    /// address does not change until the link is followed (<c>confirm-email-change</c>), within 24 hours.
    /// An address another account uses is 409 <c>EMAIL_CHANGE_CONFLICT</c>, which says no more than
    /// that. A new request replaces one still pending. The per-address mail cooldown applies, and a
    /// request it holds back answers 204 like any other.
    /// </remarks>
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HashesPassword]
    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailRequest request, CancellationToken cancellationToken)
    {
        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _profile.RequestEmailChangeAsync(userIdResult.Value, request.Password, request.NewEmail, CallerIpAddress(), cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Follows the confirm link mailed to the new address: the account switches to it.
    /// </summary>
    /// <remarks>
    /// Every session ends, as for a password change, and the old address is mailed a link that undoes
    /// the change for 7 days. Anonymous, since the link may be opened anywhere; a caller signed in as
    /// the same account gets a fresh token pair (200), anyone else 204 and signs in with the new
    /// address. A spent, expired or unknown token is 400 <c>EMAIL_CHANGE_TOKEN_INVALID</c>, and an
    /// address taken since the request is 409 <c>EMAIL_CHANGE_CONFLICT</c>.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.EmailVerification)]
    [HttpPost("confirm-email-change")]
    public async Task<ActionResult<LoginResponse>> ConfirmEmailChange(EmailChangeTokenRequest request, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        var result = await _profile.ConfirmEmailChangeAsync(request.Token, caller.IsSuccess ? caller.Value : null, CallerIpAddress(), cancellationToken);
        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        if (result.Value is not { } session)
        {
            ClearRefreshCookieIfRequested();
            return NoContent();
        }

        if (RefreshTokenCookie.IsRequested(Request) && session.RefreshToken is { } refreshToken)
        {
            _refreshCookie.Write(Response, refreshToken);
            return Ok(session with { RefreshToken = null });
        }

        return Ok(session);
    }

    /// <summary>
    /// Follows the undo link mailed to the old address after a change: the account goes back to it.
    /// </summary>
    /// <remarks>
    /// Every session ends again and any other pending change is dropped. Works for 7 days, whatever
    /// the address has been changed to since. A spent, expired or unknown token is 400
    /// <c>EMAIL_CHANGE_TOKEN_INVALID</c>; an old address another account has taken since is 409
    /// <c>EMAIL_CHANGE_CONFLICT</c>.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.EmailVerification)]
    [HttpPost("revert-email-change")]
    public async Task<IActionResult> RevertEmailChange(EmailChangeTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _profile.RevertEmailChangeAsync(request.Token, CallerIpAddress(), cancellationToken);
        if (result.IsSuccess)
        {
            ClearRefreshCookieIfRequested();
        }

        return MapResult(result);
    }

    /// <summary>
    /// Deletes the signed-in account, after the password is entered again.
    /// </summary>
    /// <remarks>
    /// Tenants only this person belongs to are deleted with their workspaces, and everything in them;
    /// they leave every other tenant, and the workspaces they own there pass to another administrator.
    /// While nobody else administers a tenant others belong to, the whole deletion is
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
        if (result.IsSuccess)
        {
            ClearRefreshCookieIfRequested();
        }

        return MapResult(result);
    }

    private void ClearRefreshCookieIfRequested()
    {
        if (RefreshTokenCookie.IsRequested(Request))
        {
            _refreshCookie.Clear(Response);
        }
    }

    private string CallerIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
