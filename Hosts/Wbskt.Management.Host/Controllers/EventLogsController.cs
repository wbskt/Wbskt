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
public sealed class EventLogsController : ControllerBase
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
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of event logs.</returns>
    [HttpGet]
    public async Task<ActionResult<ListResponse<EventLogResponse>>> GetLogs(
        Guid workspaceRef,
        [FromQuery] string? eventName,
        [FromQuery] EventCriticality? criticality,
        [FromQuery] Guid? policyRefId,
        [FromQuery] Guid? clientRefId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetLogs requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.LogsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<EventLogResponse>>.Failure(workspaceIdResult.Error));
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
        
        var result = await _eventLogService.GetLogsAsync(workspaceIdResult.Value, eventName, criticality, policyId, clientId, null, skip, take, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<EventLogResponse>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());

        return Ok(new ListResponse<EventLogResponse>
        {
            Items = result.Value
        });
    }

    private IActionResult MapResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return MapError(result.Error);
    }

    private ActionResult<T> MapResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapError(result.Error);
    }

    private ActionResult MapError(Error error)
    {
        _logger.LogWarning("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);
        return error.Type switch
        {
            ErrorType.Validation => BadRequest(error),
            ErrorType.NotFound => NotFound(error),
            ErrorType.Conflict => Conflict(error),
            ErrorType.Unauthorized => Unauthorized(error),
            _ => BadRequest(error)
        };
    }
}
