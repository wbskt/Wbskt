using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>What the workspace's plan allows and what it and its account have used (Docs/Billing.md).</summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/usage")]
[ApiController]
[Authorize]
[RequiresPermission(PermissionNames.BillingRead)]
public sealed class WorkspaceUsageController : ApiControllerBase
{
    private readonly IWorkspaceUsageService _usage;

    public WorkspaceUsageController(IWorkspaceUsageService usage)
    {
        _usage = usage;
    }

    /// <summary>
    /// The plan, this month's use pooled across the account and this workspace's share, and the
    /// workspace's credits per day from <c>from</c> to <c>to</c> (default: the last 31 days, at most 366).
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    [HttpGet]
    public async Task<ActionResult<WorkspaceUsageResponse>> Get([FromWorkspace] int workspaceId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        return MapResult(await _usage.GetAsync(workspaceId, from, to, ct));
    }
}
