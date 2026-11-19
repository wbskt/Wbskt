using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Freezing a tenant, for the platform admin host (wbskt/Wbskt.Admin). Only the admin key opens this
/// route, and without it configured it answers 404.
/// </summary>
[ApiController]
[AllowAnonymous]
[AdminApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public class AdminTenantFreezeController : ApiControllerBase
{
    private readonly IAdminFreezeService _adminFreezeService;

    public AdminTenantFreezeController(IAdminFreezeService adminFreezeService)
    {
        _adminFreezeService = adminFreezeService;
    }

    /// <summary>Freezes the tenant, or lifts it; 409 when it is already in that state.</summary>
    [HttpPut(InternalRoutes.AdminTenantFreeze)]
    public async Task<ActionResult<StaffActionResponse>> SetFrozen(Guid tenantRef, [FromBody] AdminSetTenantFrozenRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminFreezeService.SetFrozenAsync(tenantRef, request, cancellationToken));
    }
}
