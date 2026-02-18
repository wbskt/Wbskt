using Webskt.Common.Abstraction.Models.Management;
using Microsoft.AspNetCore.Mvc;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Common.Abstraction.Models;
using Webskt.Management.Host.Services;

namespace Webskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/registration-policies")]
[ApiController]
public class RegistrationPoliciesController : ControllerBase
{
    private readonly IRegistrationPolicyService _policyService;
    private readonly IReferenceMapper _workspaceMapper;

    public RegistrationPoliciesController(
        IRegistrationPolicyService policyService,
        [FromKeyedServices("Workspace")] IReferenceMapper workspaceMapper)
    {
        _policyService = policyService;
        _workspaceMapper = workspaceMapper;
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
        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(workspaceRef, cancellationToken);
        if (workspaceId <= 0) throw new SecurityException("Access denied.");

        var pagedData = await _policyService.GetAllAsync(autoApproval, name, skip, take, cancellationToken);

        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());

        return new ListResponse<RegistrationPolicyResponse>
        {
            Items = pagedData
        };
    }

    [HttpGet("{refId:guid}")]
    public async Task<RegistrationPolicyResponse> Get(Guid workspaceRef, Guid refId, CancellationToken cancellationToken)
    {
        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(workspaceRef, cancellationToken);
        if (workspaceId <= 0) throw new SecurityException("Access denied.");

        return await _policyService.GetByRefIdAsync(refId, cancellationToken);
    }

    [HttpPost]
    public async Task<RegistrationPolicyResponse> Create(Guid workspaceRef, RegistrationPolicyRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(workspaceRef, cancellationToken);
        if (workspaceId <= 0) throw new SecurityException("Access denied.");

        return await _policyService.CreateAsync(workspaceId, request, cancellationToken);
    }
}
