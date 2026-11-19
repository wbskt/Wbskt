using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Hubs;
using Wbskt.Management.Host.Services.Stats;
using Wbskt.Management.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>
/// The workspace's client messages: the current rate, so a status bar has a value before the first
/// <c>OnWorkspaceMessageRateEvent</c> arrives, and counts per hour or day for a chart.
/// </summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/stats/messages")]
[ApiController]
[Authorize]
[RequiresPermission(PermissionNames.ClientsRead)]
public sealed class WorkspaceMessageStatsController : ApiControllerBase
{
    private readonly WorkspaceMessageCounter _counter;
    private readonly IWorkspaceMessageHistoryService _history;

    public WorkspaceMessageStatsController(WorkspaceMessageCounter counter, IWorkspaceMessageHistoryService history)
    {
        _counter = counter;
        _history = history;
    }

    /// <summary>The rate over the last closed window; zero when the workspace has had no traffic since this host started.</summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    [HttpGet]
    public ActionResult<WorkspaceMessageRate> Get([FromWorkspace] int workspaceId)
    {
        return Ok(_counter.Current(workspaceId));
    }

    /// <summary>
    /// Messages clients sent and commands delivered to them, per UTC hour or day, every bucket in the range
    /// listed. Hourly counts go back 90 days, daily ones as long as the workspace exists.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="from">Start of the range (inclusive), widened to a whole bucket. Defaults to a day before <paramref name="to"/>.</param>
    /// <param name="to">End of the range (exclusive), widened to a whole bucket. Defaults to now.</param>
    /// <param name="bucket"><c>1h</c> (a range of at most 31 days) or <c>1d</c> (at most 366). Hours up to two days, days beyond, when left out.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("history")]
    public async Task<ActionResult<WorkspaceMessageHistory>> GetHistory([FromWorkspace] int workspaceId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? bucket, CancellationToken ct)
    {
        return MapResult(await _history.GetAsync(workspaceId, from, to, bucket, ct));
    }
}
