using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/registration-policies")]
[ApiController]
[Authorize]
public class RegistrationPoliciesController : ApiControllerBase
{
    private readonly IRegistrationPolicyService _policyService;
    private readonly IAuthServiceClient _authClient;
    private readonly IReferenceMapper _policyMapper;
    private readonly ILogger<RegistrationPoliciesController> _logger;

    public RegistrationPoliciesController(
        IRegistrationPolicyService policyService,
        IAuthServiceClient authClient, 
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper,
        ILogger<RegistrationPoliciesController> logger)
    {
        _policyService = policyService;
        _authClient = authClient;
        _policyMapper = policyMapper;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all registration policies for a specific workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="autoApproval">Optional filter for auto-approval status.</param>
    /// <param name="name">Optional filter for policy name (partial match).</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of registration policies.</returns>
    [HttpGet]
    public async Task<ActionResult<ListResponse<RegistrationPolicyResponse>>> GetAll(
        Guid workspaceRef,
        [FromQuery] bool? autoApproval,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("API: GetAll registration policies requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.PoliciesRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<ListResponse<RegistrationPolicyResponse>>.Failure(workspaceIdResult.Error));
        }

        var result = await _policyService.GetAllAsync(workspaceIdResult.Value, autoApproval, name, Paging.Skip(skip), Paging.Take(take), cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<ListResponse<RegistrationPolicyResponse>>.Failure(result.Error));
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());

        return Ok(new ListResponse<RegistrationPolicyResponse>
        {
            Items = result.Value
        });
    }

    /// <summary>
    /// Retrieves a specific registration policy by its reference ID.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="refId">The unique reference ID of the policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registration policy details.</returns>
    [HttpGet("{refId:guid}")]
    public async Task<ActionResult<RegistrationPolicyResponse>> Get(Guid workspaceRef, Guid refId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("API: Get registration policy requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.PoliciesRead, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<RegistrationPolicyResponse>.Failure(workspaceIdResult.Error));
        }
        
        var policyId = await _policyMapper.FindIdByRefIdAsync(refId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        var result = await _policyService.GetByIdAsync(policyId, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<RegistrationPolicyResponse>.Failure(result.Error));
        }

        if (result.Value.WorkspaceId != workspaceIdResult.Value)
        {
            _logger.LogWarning("Access denied: Policy ID {PolicyId} does not belong to Workspace ID {WorkspaceId}", policyId, workspaceIdResult.Value);
            return MapError(Error.Forbidden("POLICY_UNAUTHORIZED", "Policy does not belong to the specified workspace."));
        }
        
        return Ok(new RegistrationPolicyResponse(
            result.Value.RefId,
            result.Value.Pin,
            result.Value.Name,
            result.Value.MaxClients,
            result.Value.AutoApproval,
            result.Value.IsEnabled,
            result.Value.CreatedAt,
            result.Value.RegisteredClientCount,
            result.Value.ConnectedClientCount
        ));
    }

    /// <summary>
    /// Creates a new registration policy for a specific workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="request">The policy creation request details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created registration policy details.</returns>
    [HttpPost]
    public async Task<ActionResult<RegistrationPolicyResponse>> Create(Guid workspaceRef, RegistrationPolicyRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("API: Create registration policy requested for WorkspaceRef: '{WorkspaceRef}' (Name: '{PolicyName}')", workspaceRef, request.Name);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.PoliciesManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<RegistrationPolicyResponse>.Failure(workspaceIdResult.Error));
        }

        var result = await _policyService.CreateAsync(workspaceIdResult.Value, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Updates an existing registration policy's details.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="refId">The unique reference ID of the policy to update.</param>
    /// <param name="request">The updated policy details (Name, AutoApproval, IsEnabled).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPatch("{refId:guid}")]
    public async Task<IActionResult> Update(Guid workspaceRef, Guid refId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("API: Update registration policy requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.PoliciesManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }
        
        var policyId = await _policyMapper.FindIdByRefIdAsync(refId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        var result = await _policyService.UpdateAsync(workspaceIdResult.Value, policyId, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Replaces a policy's PIN, so a leaked one stops registering devices. Devices already registered
    /// keep working; the PIN is only used to register.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="refId">The unique reference ID of the policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The policy with its new PIN.</returns>
    [HttpPost("{refId:guid}/rotate-pin")]
    public async Task<ActionResult<RegistrationPolicyResponse>> RotatePin(Guid workspaceRef, Guid refId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("API: Rotate PIN requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.PoliciesManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result<RegistrationPolicyResponse>.Failure(workspaceIdResult.Error));
        }

        var policyId = await _policyMapper.FindIdByRefIdAsync(refId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        var result = await _policyService.RotatePinAsync(workspaceIdResult.Value, policyId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Disables a specific registration policy.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="refId">The unique reference ID of the policy to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{refId:guid}/disable")]
    public async Task<IActionResult> Disable(Guid workspaceRef, Guid refId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("API: Disable registration policy requested for WorkspaceRef: '{WorkspaceRef}', RefId: '{RefId}'", workspaceRef, refId);

        var workspaceIdResult = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.PoliciesManage, cancellationToken);
        if (workspaceIdResult.IsFailure)
        {
            return MapResult(Result.Failure(workspaceIdResult.Error));
        }
        
        var policyId = await _policyMapper.FindIdByRefIdAsync(refId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        var result = await _policyService.DisableAsync(workspaceIdResult.Value, policyId, cancellationToken);
        return MapResult(result);
    }



}
