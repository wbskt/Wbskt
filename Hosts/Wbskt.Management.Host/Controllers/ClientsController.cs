using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/clients")]
[ApiController]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly IReferenceMapper _clientMapper;
    private readonly IAuthServiceClient _authClient;
    private readonly IEventBus _eventBus;
    private readonly IReferenceMapper _policyMapper;
    private readonly IRegistrationPolicyService _policyService;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(
        IClientService clientService,
        [FromKeyedServices(ReferenceType.Client)] IReferenceMapper clientMapper,
        IAuthServiceClient authClient, 
        IEventBus eventBus,
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper,
        IRegistrationPolicyService policyService,
        ILogger<ClientsController> logger)
    {
        _clientService = clientService;
        _clientMapper = clientMapper;
        _authClient = authClient;
        _eventBus = eventBus;
        _policyMapper = policyMapper;
        _policyService = policyService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all clients for a specific workspace with optional filtering.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="status">Optional filter by client status (Pending, Registered, etc.).</param>
    /// <param name="name">Optional filter by client name (partial match).</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of clients.</returns>
    [HttpGet]
    public async Task<ActionResult<ListResponse<ClientResponse>>> GetAll(
        Guid workspaceRef,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetAll requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);
        
        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(workspaceIdResult.Error));
        }

        var result = await _clientService.GetAllAsync(workspaceIdResult.Value, status, name, skip, take, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());

        return Ok(new ListResponse<ClientResponse>
        {
            Items = result.Value
        });
    }

    /// <summary>
    /// Retrieves all clients associated with a specific registration policy.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="policyRefId">The unique reference ID of the registration policy.</param>
    /// <param name="status">Optional filter by client status.</param>
    /// <param name="name">Optional filter by client name.</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of clients linked to the specified policy.</returns>
    [HttpGet("policy/{policyRefId:guid}")]
    public async Task<ActionResult<ListResponse<ClientResponse>>> GetByPolicy(
        Guid workspaceRef,
        Guid policyRefId,
        [FromQuery] ClientStatus? status,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetByPolicy requested for WorkspaceRef: '{WorkspaceRef}', PolicyRefId: '{PolicyRefId}'", workspaceRef, policyRefId);

        // 1. Authorize workspace access
        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(workspaceIdResult.Error));
        }

        // 2. Resolve Policy RefId
        var policyId = await _policyMapper.FindIdByRefIdAsync(policyRefId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        // 3. Verify Policy belongs to Workspace
        var policyResult = await _policyService.GetByIdAsync(policyId, cancellationToken);
        if (policyResult.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(policyResult.Error));
        }

        if (policyResult.Value.WorkspaceId != workspaceIdResult.Value)
        {
            _logger.LogWarning("Access denied: Policy ID {PolicyId} does not belong to Workspace ID {WorkspaceId}", policyId, workspaceIdResult.Value);
            return MapError(Error.Unauthorized("POLICY_UNAUTHORIZED", "Policy does not belong to the specified workspace."));
        }

        // 4. All checks pass, get the data
        var result = await _clientService.GetByPolicyIdAsync(workspaceIdResult.Value, policyId, status, name, skip, take, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<ClientResponse>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());
        return Ok(new ListResponse<ClientResponse> { Items = result.Value });
    }

    /// <summary>
    /// Updates the status of a specific client (e.g., Revoking or Approving a client).
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the client to update.</param>
    /// <param name="request">The new status details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPatch("{clientRefId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid workspaceRef, Guid clientRefId, UpdateClientStatusRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: UpdateStatus requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}' to Status: '{Status}'", workspaceRef, clientRefId, request.Status);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsUpdate, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var id = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);
        if (id <= 0)
        {
            return NotFound(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
        }

        var result = await _clientService.UpdateStatusAsync(workspaceIdResult.Value, id, request.Status, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Sends an asynchronous command payload to a specific registered client.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the target client.</param>
    /// <param name="request">The command name and payload data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{clientRefId:guid}/command")]
    public async Task<IActionResult> SendCommand(Guid workspaceRef, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: SendCommand requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsCommand, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var clientId = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);
        if (clientId <= 0)
        {
            return NotFound(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
        }

        await _eventBus.PublishAsync(new ClientCommandEvent(clientRefId, clientId, workspaceIdResult.Value, request.Type, request.Payload), cancellationToken);
        _logger.LogInformation("Successfully published client command event for ClientRefId: '{ClientRefId}'", clientRefId);
        return NoContent();
    }

    /// <summary>
    /// Triggers a ping event for a specific client to verify connectivity or wake state.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="clientRefId">The unique reference ID of the target client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{clientRefId:guid}/ping")]
    public async Task<IActionResult> Ping(Guid workspaceRef, Guid clientRefId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Ping requested for WorkspaceRef: '{WorkspaceRef}', ClientRefId: '{ClientRefId}'", workspaceRef, clientRefId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.ClientsPing, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }

        var clientId = await _clientMapper.FindIdByRefIdAsync(clientRefId, cancellationToken);
        if (clientId <= 0)
        {
            return NotFound(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
        }

        await _eventBus.PublishAsync(new ClientPingEvent(clientRefId, clientId, workspaceIdResult.Value, DateTime.UtcNow), cancellationToken);
        _logger.LogInformation("Successfully published client ping event for ClientRefId: '{ClientRefId}'", clientRefId);
        return NoContent();
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

public record UpdateClientStatusRequest(ClientStatus Status);

public record ClientCommandRequest(string Type, string Payload);