using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
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
public sealed class EventLogsController : ControllerBase
{
    private readonly IEventLogService _eventLogService;
    private readonly IAuthServiceClient _authClient;
    private readonly IReferenceMapper _policyMapper;
    private readonly IReferenceMapper _clientMapper;

    public EventLogsController(IEventLogService eventLogService, IAuthServiceClient authClient, 
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper,
        [FromKeyedServices(ReferenceType.Client)] IReferenceMapper clientMapper
        )
    {
        _eventLogService = eventLogService;
        _authClient = authClient;
        _policyMapper = policyMapper;
        _clientMapper = clientMapper;
    }

    /// <summary>
    /// Retrieves a list of system and client event logs for a specific workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="eventName">Optional filter for a specific event name.</param>
    /// <param name="criticality">Optional filter by criticality (Information, Warning, Critical).</param>
    /// <param name="policyRefId"></param>
    /// <param name="clientRefId"></param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of event logs.</returns>
    [HttpGet]
    public async Task<ListResponse<EventLogResponse>> GetLogs(
        Guid workspaceRef,
        [FromQuery] string? eventName,
        [FromQuery] EventCriticality? criticality,
        [FromQuery] Guid? policyRefId,
        [FromQuery] Guid? clientRefId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.LogsRead, cancellationToken);
        int? clientId = null;
        int? policyId = null;

        if (policyRefId.HasValue)
        {
            policyId = await _policyMapper.FindIdByRefIdAsync(policyRefId.Value, cancellationToken);
        }
        
        if (clientRefId.HasValue)
        {
            clientId = await _clientMapper.FindIdByRefIdAsync(clientRefId.Value, cancellationToken);
        }
        
        var pagedList = await _eventLogService.GetLogsAsync(workspaceId, eventName, criticality, policyId, clientId, null, skip, take, cancellationToken);
        
        Response.Headers.Append("X-Total-Count", pagedList.TotalCount.ToString());

        return new ListResponse<EventLogResponse>()
        {
            Items = pagedList
        };
    }
}
