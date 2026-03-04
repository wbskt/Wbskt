using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Management.Host.Models;
using Webskt.Management.Host.Services;
using Webskt.Management.Host.Services.Clients;
using Webskt.Workflow.Abstraction.Models;

namespace Webskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/workflows")]
[ApiController]
[Authorize]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IWorkflowService _workflowService;
    private readonly IAuthServiceClient _authClient;
    private readonly IReferenceMapper _workflowMapper;

    public WorkflowsController(
        IWorkflowService workflowService, 
        IAuthServiceClient authClient,
        [FromKeyedServices("Workflow")] IReferenceMapper workflowMapper)
    {
        _workflowService = workflowService;
        _authClient = authClient;
        _workflowMapper = workflowMapper;
    }

    [HttpGet]
    public async Task<IReadOnlyCollection<WorkflowSummaryResponse>> GetAll(Guid workspaceRef, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "workflows.read", cancellationToken);
        
        return await _workflowService.GetAllAsync(workspaceId, cancellationToken);
    }

    [HttpGet("{workflowRefId:guid}")]
    public async Task<WorkflowDefinition> Get(Guid workspaceRef, Guid workflowRefId, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "workflows.read", cancellationToken);
        var id = await _workflowMapper.FindIdByRefIdAsync(workflowRefId, cancellationToken);
        
        if (id <= 0)
        {
            throw new SecurityException("Access denied.");
        }

        return await _workflowService.GetByIdAsync(workspaceId, id, cancellationToken);
    }

    [HttpPost]
    public async Task<WorkflowSummaryResponse> Create(Guid workspaceRef, CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "workflows.create", cancellationToken);
        
        return await _workflowService.CreateAsync(workspaceId, request, cancellationToken);
    }

    [HttpPut("{workflowRefId:guid}")]
    public async Task Update(Guid workspaceRef, Guid workflowRefId, UpdateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "workflows.update", cancellationToken);
        var id = await _workflowMapper.FindIdByRefIdAsync(workflowRefId, cancellationToken);
        
        if (id <= 0)
        {
            throw new SecurityException("Access denied.");
        }

        await _workflowService.UpdateAsync(workspaceId, id, request, cancellationToken);
    }

    [HttpDelete("{workflowRefId:guid}")]
    public async Task Delete(Guid workspaceRef, Guid workflowRefId, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, "workflows.delete", cancellationToken);
        var id = await _workflowMapper.FindIdByRefIdAsync(workflowRefId, cancellationToken);
        
        if (id <= 0)
        {
            throw new SecurityException("Access denied.");
        }

        await _workflowService.DeleteAsync(workspaceId, id, cancellationToken);
    }
}
