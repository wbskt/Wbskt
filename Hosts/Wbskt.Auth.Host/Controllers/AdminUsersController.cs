using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// People's accounts, for the platform admin host (wbskt/Wbskt.Admin). Only the admin key opens these
/// routes, and without it configured they answer 404, so a host deployed without the key is unchanged.
/// </summary>
[ApiController]
[AllowAnonymous]
[AdminApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public class AdminUsersController : ApiControllerBase
{
    private readonly IAdminUserService _adminUserService;

    public AdminUsersController(IAdminUserService adminUserService)
    {
        _adminUserService = adminUserService;
    }

    /// <summary>Lifts the person's sign-in lockout; 409 <c>USER_NOT_LOCKED</c> when there is none.</summary>
    [HttpPost(InternalRoutes.AdminUserLockoutClear)]
    public async Task<ActionResult<StaffActionResponse>> ClearLockout(Guid userRef, [FromBody] AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminUserService.ClearLockoutAsync(userRef, request, cancellationToken));
    }

    /// <summary>Mails a new verification link; 409 when the address is verified or sign-in is off.</summary>
    [HttpPost(InternalRoutes.AdminUserVerificationResend)]
    public async Task<ActionResult<StaffActionResponse>> ResendVerification(Guid userRef, [FromBody] AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminUserService.ResendVerificationAsync(userRef, request, cancellationToken));
    }

    /// <summary>Mails a password reset link; 409 when sign-in is off.</summary>
    [HttpPost(InternalRoutes.AdminUserPasswordReset)]
    public async Task<ActionResult<StaffActionResponse>> SendPasswordReset(Guid userRef, [FromBody] AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminUserService.SendPasswordResetAsync(userRef, request, cancellationToken));
    }

    /// <summary>Ends every session the person has, their access tokens included.</summary>
    [HttpPost(InternalRoutes.AdminUserSessionsRevoke)]
    public async Task<ActionResult<StaffActionResponse>> SignOutEverywhere(Guid userRef, [FromBody] AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminUserService.SignOutEverywhereAsync(userRef, request, cancellationToken));
    }

    /// <summary>Turns the person's sign-in on or off; off also ends every session.</summary>
    [HttpPut(InternalRoutes.AdminUserSignIn)]
    public async Task<ActionResult<StaffActionResponse>> SetSignIn(Guid userRef, [FromBody] AdminSetUserSignInRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminUserService.SetSignInAsync(userRef, request, cancellationToken));
    }
}
