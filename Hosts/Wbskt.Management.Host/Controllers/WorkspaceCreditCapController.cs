using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>A workspace's monthly credit cap, out of its account's shared credits (Docs/Billing.md).</summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/credit-cap")]
[ApiController]
[Authorize]
[RequiresPermission(PermissionNames.BillingManage)]
public sealed class WorkspaceCreditCapController : ApiControllerBase
{
    private readonly IWorkspaceCreditCapService _caps;

    public WorkspaceCreditCapController(IWorkspaceCreditCapService caps)
    {
        _caps = caps;
    }

    /// <summary>
    /// Sets the cap (<c>{ monthlyCreditCap }</c>), or removes it with null. <c>capReached</c> in the
    /// response says the workspace has already spent that much this month, so its new runs are refused.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    [HttpPut]
    public async Task<ActionResult<WorkspaceCreditCapResponse>> Put([FromWorkspace] int workspaceId, [FromBody] WorkspaceCreditCapRequest request, CancellationToken ct)
    {
        return MapResult(await _caps.SetAsync(workspaceId, request, ct));
    }
}
