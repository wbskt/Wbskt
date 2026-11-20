using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Services.Stats;
using Wbskt.Management.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>What a workspace holds, for its card on the workspaces list and the Overview.</summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/counts")]
[ApiController]
[Authorize]
[RequiresPermission(PermissionNames.ClientsRead, PermissionNames.PoliciesRead, PermissionNames.WorkflowsRead)]
public sealed class WorkspaceCountsController : ApiControllerBase
{
    private readonly IWorkspaceCountsService _counts;

    public WorkspaceCountsController(IWorkspaceCountsService counts)
    {
        _counts = counts;
    }

    /// <summary>The workspace's clients by state, its policies, and its workflows by state (deleted ones left out).</summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<WorkspaceCountsResponse>> Get([FromWorkspace] int workspaceId, CancellationToken ct)
    {
        return await _counts.GetAsync(workspaceId, ct);
    }
}
