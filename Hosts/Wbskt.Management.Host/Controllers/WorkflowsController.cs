using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Host.Controllers;

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

    /// <summary>
    /// Retrieves all workflows summaries for a specific workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of workflow summaries.</returns>
    [HttpGet]
    public async Task<ListResponse<WorkflowSummaryResponse>> GetAll(Guid workspaceRef, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, cancellationToken);
        var pagedData = await _workflowService.GetAllAsync(workspaceId, cancellationToken);
        
        Response.Headers.Append("X-Total-Count", pagedData.TotalCount.ToString());

        return new ListResponse<WorkflowSummaryResponse>
        {
            Items = pagedData
        };
    }

    /// <summary>
    /// Retrieves the detailed definition of a specific workflow.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="workflowRefId">The unique reference ID of the workflow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full workflow definition including nodes and edges.</returns>
    /// <exception cref="SecurityException">Thrown if the workflow reference is invalid or access is denied.</exception>
    [HttpGet("{workflowRefId:guid}")]
    public async Task<WorkflowDefinition> Get(Guid workspaceRef, Guid workflowRefId, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, cancellationToken);
        var id = await _workflowMapper.FindIdByRefIdAsync(workflowRefId, cancellationToken);
        
        if (id <= 0)
        {
            throw new SecurityException("Access denied.");
        }

        return await _workflowService.GetByIdAsync(workspaceId, id, cancellationToken);
    }

    /// <summary>
    /// Creates a new workflow summary in the specified workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="request">The workflow creation details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summary of the newly created workflow.</returns>
    [HttpPost]
    public async Task<WorkflowSummaryResponse> Create(Guid workspaceRef, CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsCreate, cancellationToken);
        
        return await _workflowService.CreateAsync(workspaceId, request, cancellationToken);
    }

    /// <summary>
    /// Updates an existing workflow's definition, including its logic nodes and connections.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="workflowRefId">The unique reference ID of the workflow to update.</param>
    /// <param name="request">The updated workflow definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="SecurityException">Thrown if the workflow reference is invalid or access is denied.</exception>
    [HttpPut("{workflowRefId:guid}")]
    public async Task Update(Guid workspaceRef, Guid workflowRefId, UpdateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsUpdate, cancellationToken);
        var id = await _workflowMapper.FindIdByRefIdAsync(workflowRefId, cancellationToken);
        
        if (id <= 0)
        {
            throw new SecurityException("Access denied.");
        }

        await _workflowService.UpdateAsync(workspaceId, id, request, cancellationToken);
    }

    /// <summary>
    /// Deletes a specific workflow from the workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="workflowRefId">The unique reference ID of the workflow to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="SecurityException">Thrown if the workflow reference is invalid or access is denied.</exception>
    [HttpDelete("{workflowRefId:guid}")]
    public async Task Delete(Guid workspaceRef, Guid workflowRefId, CancellationToken cancellationToken)
    {
        var workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsDelete, cancellationToken);
        var id = await _workflowMapper.FindIdByRefIdAsync(workflowRefId, cancellationToken);
        
        if (id <= 0)
        {
            throw new SecurityException("Access denied.");
        }

        await _workflowService.DeleteAsync(workspaceId, id, cancellationToken);
    }
}
