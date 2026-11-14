using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// A tenant's members, for the platform admin host (wbskt/Wbskt.Admin). Only the admin key opens these
/// routes, and without it configured they answer 404, so a host deployed without the key is unchanged.
/// </summary>
[ApiController]
[AllowAnonymous]
[AdminApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public class AdminTenantMembersController : ApiControllerBase
{
    private readonly IAdminMemberService _adminMemberService;

    public AdminTenantMembersController(IAdminMemberService adminMemberService)
    {
        _adminMemberService = adminMemberService;
    }

    /// <summary>Suspends the member in this tenant, or lifts it; 409 when that would leave no administrator.</summary>
    [HttpPut(InternalRoutes.AdminTenantMemberSuspension)]
    public async Task<ActionResult<StaffActionResponse>> SetSuspended(Guid tenantRef, Guid userRef, [FromBody] AdminSetMemberSuspendedRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminMemberService.SetSuspendedAsync(tenantRef, userRef, request, cancellationToken));
    }

    /// <summary>Removes the member from this tenant; 409 when that would leave it without an administrator or them in no tenant.</summary>
    [HttpPost(InternalRoutes.AdminTenantMemberRemove)]
    public async Task<ActionResult<StaffActionResponse>> Remove(Guid tenantRef, Guid userRef, [FromBody] AdminRemoveMemberRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminMemberService.RemoveAsync(tenantRef, userRef, request, cancellationToken));
    }
}
