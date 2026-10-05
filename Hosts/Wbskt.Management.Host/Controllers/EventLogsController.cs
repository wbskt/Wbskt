using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
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
    private readonly IAuthServiceClient _authClient;
    private readonly IReferenceMapper _policyMapper;
    private readonly IReferenceMapper _clientMapper;
    private readonly ILogger<EventLogsController> _logger;

    public EventLogsController(
        IEventLogService eventLogService, 
        IAuthServiceClient authClient, 
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper,
        [FromKeyedServices(ReferenceType.Client)] IReferenceMapper clientMapper,
        ILogger<EventLogsController> logger)
    {
        _eventLogService = eventLogService;
        _authClient = authClient;
        _policyMapper = policyMapper;
        _clientMapper = clientMapper;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a list of system and client event logs for a specific workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="eventName">Optional filter for a specific event name.</param>
    /// <param name="criticality">Optional filter by criticality (Information, Warning, Critical).</param>
    /// <param name="policyRefId"></param>
    /// <param name="clientRefId"></param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page; omit for the newest entries.</param>
    /// <param name="take">Page size, 1 to 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of event logs, newest first, with the cursor for the next page.</returns>
    [HttpGet]
    public async Task<ActionResult<EventLogListResponse>> GetLogs(
        Guid workspaceRef,
        [FromQuery] string? eventName,
        [FromQuery] EventCriticality? criticality,
        [FromQuery] Guid? policyRefId,
        [FromQuery] Guid? clientRefId,
        [FromQuery] long? cursor = null,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("API: GetLogs requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.LogsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<EventLogListResponse>.Failure(workspaceIdResult.Error));
        }

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
        
        return MapResult(await _eventLogService.GetLogsAsync(workspaceIdResult.Value, eventName, criticality, policyId, clientId, null, cursor, take, cancellationToken));
    }
}
