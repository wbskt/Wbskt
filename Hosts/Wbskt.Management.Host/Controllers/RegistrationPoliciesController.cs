using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Authorization;
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
    private readonly IReferenceMapper _policyMapper;

    public RegistrationPoliciesController(
        IRegistrationPolicyService policyService,
        [FromKeyedServices(ReferenceType.RegistrationPolicy)] IReferenceMapper policyMapper)
    {
        _policyService = policyService;
        _policyMapper = policyMapper;
    }

    /// <summary>
    /// Retrieves all registration policies for a specific workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="autoApproval">Optional filter for auto-approval status.</param>
    /// <param name="name">Optional filter for policy name (partial match).</param>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of registration policies.</returns>
    [HttpGet]
    [RequiresPermission(PermissionNames.PoliciesRead)]
    public async Task<ActionResult<ListResponse<RegistrationPolicyResponse>>> GetAll(
        [FromWorkspace] int workspaceId,
        [FromQuery] bool? autoApproval,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _policyService.GetAllAsync(workspaceId, autoApproval, name, Paging.Skip(skip), Paging.Take(take), cancellationToken);
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
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="refId">The unique reference ID of the policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registration policy details.</returns>
    [HttpGet("{refId:guid}")]
    [RequiresPermission(PermissionNames.PoliciesRead)]
    public async Task<ActionResult<RegistrationPolicyResponse>> Get([FromWorkspace] int workspaceId, Guid refId, CancellationToken cancellationToken)
    {
        var result = await _policyService.FindInWorkspaceAsync(workspaceId, refId, cancellationToken);
        if (result.IsFailure)
        {
            return MapResult(Result<RegistrationPolicyResponse>.Failure(result.Error));
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
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="request">The policy creation request details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created registration policy details.</returns>
    [HttpPost]
    [RequiresPermission(PermissionNames.PoliciesManage)]
    public async Task<ActionResult<RegistrationPolicyResponse>> Create([FromWorkspace] int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken)
    {
        var result = await _policyService.CreateAsync(workspaceId, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Updates an existing registration policy's details.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="refId">The unique reference ID of the policy to update.</param>
    /// <param name="request">The updated policy details (Name, AutoApproval, IsEnabled).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPatch("{refId:guid}")]
    [RequiresPermission(PermissionNames.PoliciesManage)]
    public async Task<IActionResult> Update([FromWorkspace] int workspaceId, Guid refId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken)
    {
        var policyId = await _policyMapper.FindIdByRefIdAsync(refId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        var result = await _policyService.UpdateAsync(workspaceId, policyId, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Replaces a policy's PIN, so a leaked one stops registering devices. Devices already registered
    /// keep working; the PIN is only used to register.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="refId">The unique reference ID of the policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The policy with its new PIN.</returns>
    [HttpPost("{refId:guid}/rotate-pin")]
    [RequiresPermission(PermissionNames.PoliciesManage)]
    public async Task<ActionResult<RegistrationPolicyResponse>> RotatePin([FromWorkspace] int workspaceId, Guid refId, CancellationToken cancellationToken)
    {
        var policyId = await _policyMapper.FindIdByRefIdAsync(refId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        var result = await _policyService.RotatePinAsync(workspaceId, policyId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Disables a specific registration policy.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="refId">The unique reference ID of the policy to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{refId:guid}/disable")]
    [RequiresPermission(PermissionNames.PoliciesManage)]
    public async Task<IActionResult> Disable([FromWorkspace] int workspaceId, Guid refId, CancellationToken cancellationToken)
    {
        var policyId = await _policyMapper.FindIdByRefIdAsync(refId, cancellationToken);
        if (policyId <= 0)
        {
            return NotFound(Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found."));
        }

        var result = await _policyService.DisableAsync(workspaceId, policyId, cancellationToken);
        return MapResult(result);
    }



}
