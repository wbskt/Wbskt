using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/event-logs")]
[ApiController]
[Authorize]
public sealed class EventLogsController : ApiControllerBase
{
    private readonly IEventLogService _eventLogService;
    private readonly IRegistrationPolicyService _policyService;
    private readonly IClientQueryService _clientService;

    public EventLogsController(
        IEventLogService eventLogService,
        IRegistrationPolicyService policyService,
        IClientQueryService clientService)
    {
        _eventLogService = eventLogService;
        _policyService = policyService;
        _clientService = clientService;
    }

    /// <summary>
    /// Retrieves a list of system and client event logs for a specific workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="eventName">Optional filter for a specific event name.</param>
    /// <param name="criticality">Optional filter by criticality (Information, Warning, Critical).</param>
    /// <param name="policyRefId">Optional filter: only this policy's entries. A policy this workspace does not own is a 404.</param>
    /// <param name="clientRefId">Optional filter: only this client's entries. A client this workspace does not own is a 404.</param>
    /// <param name="page">The page to read: <c>cursor</c> (left out for the newest entries) and <c>limit</c> (default 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of event logs, newest first, with the cursor for the next page.</returns>
    [HttpGet]
    [RequiresPermission(PermissionNames.LogsRead)]
    public async Task<ActionResult<Page<EventLogResponse>>> GetLogs(
        [FromWorkspace] int workspaceId,
        [FromQuery] string? eventName,
        [FromQuery] EventCriticality? criticality,
        [FromQuery] Guid? policyRefId,
        [FromQuery] Guid? clientRefId,
        [FromQuery] PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var cursor = page.AfterKey();
        if (cursor.IsFailure)
        {
            return MapError(cursor.Error);
        }

        int? clientId = null;
        int? policyId = null;

        // A filter naming another workspace's policy or client is a 404, not an empty page.
        if (policyRefId.HasValue)
        {
            var policy = await _policyService.FindInWorkspaceAsync(workspaceId, policyRefId.Value, cancellationToken);
            if (policy.IsFailure)
            {
                return MapError(policy.Error);
            }

            policyId = policy.Value.Id;
        }

        if (clientRefId.HasValue)
        {
            var client = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRefId.Value, cancellationToken);
            if (client.IsFailure)
            {
                return MapError(client.Error);
            }

            clientId = client.Value;
        }

        return MapResult(await _eventLogService.GetLogsAsync(workspaceId, eventName, criticality, policyId, clientId, null, cursor.Value, page.LimitOr(50), cancellationToken));
    }
}
