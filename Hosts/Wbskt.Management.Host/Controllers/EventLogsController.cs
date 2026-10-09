using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/event-logs")]
[ApiController]
[Authorize]
public sealed class EventLogsController : ApiControllerBase
{
    private readonly IEventLogService _eventLogService;

    public EventLogsController(IEventLogService eventLogService)
    {
        _eventLogService = eventLogService;
    }

    /// <summary>
    /// The workspace's event log, newest first, narrowed by any of the filters in <paramref name="query"/>.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="query">The filters: event names and groups, criticality, policy, client, workflow, user, time range, device traffic and text search.</param>
    /// <param name="page">The page to read: <c>cursor</c> (left out for the newest entries) and <c>limit</c> (default 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of event logs, newest first, with the cursor for the next page.</returns>
    [HttpGet]
    [RequiresPermission(PermissionNames.LogsRead)]
    public async Task<ActionResult<Page<EventLogResponse>>> GetLogs(
        [FromWorkspace] int workspaceId,
        [FromQuery] EventLogQuery query,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var cursor = page.AfterKey();
        if (cursor.IsFailure)
        {
            return MapError(cursor.Error);
        }

        return MapResult(await _eventLogService.GetLogsAsync(workspaceId, query, cursor.Value, page.LimitOr(50), cancellationToken));
    }

    /// <summary>
    /// How many entries each group (people, clients, policies, workflows, security) has over the
    /// range, the last 7 days unless <c>from</c> and <c>to</c> say otherwise, and the total.
    /// </summary>
    [HttpGet("summary")]
    [RequiresPermission(PermissionNames.LogsRead)]
    public async Task<ActionResult<EventLogSummaryResponse>> GetSummary(
        [FromWorkspace] int workspaceId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] EventLogTraffic traffic = EventLogTraffic.Include,
        CancellationToken cancellationToken = default)
    {
        return MapResult(await _eventLogService.GetSummaryAsync(workspaceId, from, to, traffic, cancellationToken));
    }

    /// <summary>
    /// The entries <c>GET event-logs</c> would list for the same filters, as one CSV file, newest
    /// first. The range defaults to the last 7 days, and the file holds at most 100,000 rows.
    /// </summary>
    [HttpGet("csv")]
    [Produces("text/csv")]
    [RequiresPermission(PermissionNames.LogsRead)]
    public async Task<IActionResult> GetCsv(
        Guid workspaceRef,
        [FromWorkspace] int workspaceId,
        [FromQuery] EventLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var csv = await _eventLogService.GetCsvAsync(workspaceId, query, cancellationToken);
        if (csv.IsFailure)
        {
            return MapError(csv.Error);
        }

        return File(Encoding.UTF8.GetBytes(csv.Value), "text/csv; charset=utf-8", $"event-log-{workspaceRef}.csv");
    }
}
