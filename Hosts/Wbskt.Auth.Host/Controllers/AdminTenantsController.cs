using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Tenants, for the platform admin host (wbskt/Wbskt.Admin). Only the admin key opens these routes, and
/// without it configured they answer 404, so a host deployed without the key is unchanged.
/// </summary>
[ApiController]
[AllowAnonymous]
[AdminApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public class AdminTenantsController : ApiControllerBase
{
    private readonly IAdminTenantService _adminTenantService;

    public AdminTenantsController(IAdminTenantService adminTenantService)
    {
        _adminTenantService = adminTenantService;
    }

    [HttpGet(InternalRoutes.AdminTenantLimits)]
    public async Task<ActionResult<AdminTenantLimitsResponse>> GetLimits(Guid tenantRef, CancellationToken cancellationToken)
    {
        return MapResult(await _adminTenantService.GetLimitsAsync(tenantRef, cancellationToken));
    }

    /// <summary>Replaces the tenant's limit overrides alone; its plan, and whoever set it, stay in charge.</summary>
    [HttpPut(InternalRoutes.AdminTenantLimits)]
    public async Task<ActionResult<StaffActionResponse>> SetLimits(Guid tenantRef, [FromBody] AdminSetTenantLimitsRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminTenantService.SetLimitsAsync(tenantRef, request, cancellationToken));
    }

    /// <summary>Puts the tenant on a staff plan until a date; billing leaves the plan alone until then.</summary>
    [HttpPut(InternalRoutes.AdminTenantPlan)]
    public async Task<ActionResult<StaffActionResponse>> SetPlan(Guid tenantRef, [FromBody] AdminSetTenantPlanRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminTenantService.SetPlanAsync(tenantRef, request, cancellationToken));
    }
}
