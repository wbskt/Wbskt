using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Services.Admin;
using Wbskt.Management.Models;
using Wbskt.Models.Admin;

namespace Wbskt.Management.Host.Controllers;

/// <summary>
/// Workspaces, for the platform admin host (wbskt/Wbskt.Admin). Only the admin key opens these routes, and
/// without it configured they answer 404, so a host deployed without the key is unchanged.
/// </summary>
[ApiController]
[AllowAnonymous]
[AdminApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AdminWorkspacesController : ApiControllerBase
{
    private readonly IAdminWorkspaceService _adminWorkspaceService;

    public AdminWorkspacesController(IAdminWorkspaceService adminWorkspaceService)
    {
        _adminWorkspaceService = adminWorkspaceService;
    }

    /// <summary>Sets the workspace's monthly credit cap, or removes it with null, as the console's route does.</summary>
    [HttpPut(InternalRoutes.AdminWorkspaceCreditCap)]
    public async Task<ActionResult<StaffActionResponse>> SetCreditCap(Guid workspaceRef, [FromBody] AdminSetWorkspaceCreditCapRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _adminWorkspaceService.SetCreditCapAsync(workspaceRef, request, cancellationToken));
    }
}
