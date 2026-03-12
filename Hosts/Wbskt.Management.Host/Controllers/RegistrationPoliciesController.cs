using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.Foundation.Abstraction;
using Wbskt.Foundation.Abstraction.Exceptions;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/registration-policies")]
[ApiController]
[Authorize]
public class RegistrationPoliciesController : ControllerBase
{
    private readonly IRegistrationPolicyService _policyService;
    private readonly IAuthServiceClient _authClient;
    private readonly IReferenceMapper _policyMapper;

    public RegistrationPoliciesController(
        IRegistrationPolicyService policyService,
        IAuthServiceClient authClient, [FromKeyedServices("RegistrationPolicy")]IReferenceMapper policyMapper)
    {
        _policyService = policyService;
        _authClient = authClient;
        _policyMapper = policyMapper;
    }

    [HttpGet]
    public async Task<ListResponse<RegistrationPolicyResponse>> GetAll(
        Guid workspaceRef,
        [FromQuery] bool? autoApproval,
        [FromQuery] string? name,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var workSpaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "policies.read", cancellationToken);

        var pagedData = await _policyService.GetAllAsync(workSpaceId, autoApproval, name, skip, take, cancellationToken);

        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());

        return new ListResponse<RegistrationPolicyResponse>
        {
            Items = pagedData
        };
    }

    [HttpGet("{refId:guid}")]
    public async Task<RegistrationPolicyResponse> Get(Guid workspaceRef, Guid refId, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "policies.read", cancellationToken);
        
        var policyId = await _policyMapper.FindIdByRefIdAsync(refId, cancellationToken);
        if (policyId <= 0)
        {
            throw new NotFoundException("Policy not found.");
        }

        var policy = await _policyService.GetByIdAsync(policyId, cancellationToken);
        if (policy.WorkspaceId != workspaceId)
        {
            throw new SecurityException("Policy does not belong to the specified workspace.");
        }
        
        return new RegistrationPolicyResponse(
            policy.RefId,
            policy.Pin,
            policy.Name,
            policy.MaxClients,
            policy.AutoApproval,
            policy.CreatedAt
        );
    }

    [HttpPost]
    public async Task<RegistrationPolicyResponse> Create(Guid workspaceRef, RegistrationPolicyRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "policies.manage", cancellationToken);
        return await _policyService.CreateAsync(workspaceId, request, cancellationToken);
    }
}
