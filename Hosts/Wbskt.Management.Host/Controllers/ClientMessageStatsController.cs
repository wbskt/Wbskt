using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Stats;
using Wbskt.Management.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>One client's messages per hour or day, for the client page's chart.</summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/clients/{clientRef:guid}/stats/messages")]
[ApiController]
[Authorize]
[RequiresPermission(PermissionNames.ClientsRead)]
public sealed class ClientMessageStatsController : ApiControllerBase
{
    private readonly IWorkspaceMessageHistoryService _history;

    public ClientMessageStatsController(IWorkspaceMessageHistoryService history)
    {
        _history = history;
    }

    /// <summary>
    /// Messages the client sent and commands delivered to it, per UTC hour or day, every bucket in the range
    /// listed. Counts go back as far as the audit log (90 days).
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="clientRef">The unique reference ID of the client.</param>
    /// <param name="from">Start of the range (inclusive), widened to a whole bucket. Defaults to a day before <paramref name="to"/>.</param>
    /// <param name="to">End of the range (exclusive), widened to a whole bucket. Defaults to now.</param>
    /// <param name="bucket"><c>1h</c> (a range of at most 31 days) or <c>1d</c>. Hours up to two days, days beyond, when left out.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("history")]
    public async Task<ActionResult<WorkspaceMessageHistory>> GetHistory([FromWorkspace] int workspaceId, Guid clientRef, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? bucket, CancellationToken ct)
    {
        return MapResult(await _history.GetForClientAsync(workspaceId, clientRef, from, to, bucket, ct));
    }
}
