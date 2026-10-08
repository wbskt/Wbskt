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
    private readonly IReferenceMapper _policyMapper;
    private readonly IReferenceMapper _clientMapper;

    public EventLogsController(
        IEventLogService eventLogService, 
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper,
        [FromKeyedServices(ReferenceType.Client)] IReferenceMapper clientMapper)
    {
        _eventLogService = eventLogService;
        _policyMapper = policyMapper;
        _clientMapper = clientMapper;
    }

    /// <summary>
    /// Retrieves a list of system and client event logs for a specific workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="eventName">Optional filter for a specific event name.</param>
    /// <param name="criticality">Optional filter by criticality (Information, Warning, Critical).</param>
    /// <param name="policyRefId"></param>
    /// <param name="clientRefId"></param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page; omit for the newest entries.</param>
    /// <param name="take">Page size, 1 to 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of event logs, newest first, with the cursor for the next page.</returns>
    [HttpGet]
    [RequiresPermission(PermissionNames.LogsRead)]
    public async Task<ActionResult<EventLogListResponse>> GetLogs(
        [FromWorkspace] int workspaceId,
        [FromQuery] string? eventName,
        [FromQuery] EventCriticality? criticality,
        [FromQuery] Guid? policyRefId,
        [FromQuery] Guid? clientRefId,
        [FromQuery] long? cursor = null,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        int? clientId = null;
        int? policyId = null;

        if (policyRefId.HasValue)
        {
            policyId = await _policyMapper.FindIdByRefIdAsync(policyRefId.Value, cancellationToken);
            if (policyId <= 0)
            {
                return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
            }
        }
        
        if (clientRefId.HasValue)
        {
            clientId = await _clientMapper.FindIdByRefIdAsync(clientRefId.Value, cancellationToken);
            if (clientId <= 0)
            {
                return NotFound(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
            }
        }
        
        return MapResult(await _eventLogService.GetLogsAsync(workspaceId, eventName, criticality, policyId, clientId, null, cursor, take, cancellationToken));
    }
}
