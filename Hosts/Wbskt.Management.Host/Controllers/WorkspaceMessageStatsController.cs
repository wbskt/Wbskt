using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Hubs;
using Wbskt.Management.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>
/// The workspace's current client message rate, so a status bar has a value before the first
/// <c>OnWorkspaceMessageRateEvent</c> arrives.
/// </summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/stats/messages")]
[ApiController]
[Authorize]
[RequiresPermission(PermissionNames.ClientsRead)]
public sealed class WorkspaceMessageStatsController : ApiControllerBase
{
    private readonly WorkspaceMessageCounter _counter;

    public WorkspaceMessageStatsController(WorkspaceMessageCounter counter)
    {
        _counter = counter;
    }

    /// <summary>The rate over the last closed window; zero when the workspace has had no traffic since this host started.</summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    [HttpGet]
    public ActionResult<WorkspaceMessageRate> Get([FromWorkspace] int workspaceId)
    {
        return Ok(_counter.Current(workspaceId));
    }
}
