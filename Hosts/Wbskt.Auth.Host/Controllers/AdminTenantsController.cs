using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;

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
}
