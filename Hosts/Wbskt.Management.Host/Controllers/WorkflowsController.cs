using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}/workflows")]
[ApiController]
[Authorize]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IWorkflowDefinitionService _service;
    private readonly IWorkflowEngineClient _engineClient;
    private readonly IAuthServiceClient _authClient;

    public WorkflowsController(IWorkflowDefinitionService service, IWorkflowEngineClient engineClient, IAuthServiceClient authClient)
    {
        _service = service;
        _engineClient = engineClient;
        _authClient = authClient;
    }

    [HttpPost]
    public async Task<WorkflowPublishResponse> Publish(Guid workspaceRef, [FromBody] WorkflowPublishRequest request, CancellationToken ct)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsCreate, ct);
        return await _service.PublishAsync(workspaceId, request, ct);
    }

    [HttpGet("{refId:guid}")]
    public async Task<WorkflowDefinitionDto> GetCurrent(Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        return await _service.GetCurrentAsync(workspaceId, refId, ct);
    }

    [HttpGet("{refId:guid}/versions/{version:int}")]
    public async Task<WorkflowDefinitionDto> GetVersion(Guid workspaceRef, Guid refId, int version, CancellationToken ct)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        return await _service.GetVersionAsync(workspaceId, refId, version, ct);
    }

    [HttpPost("{refId:guid}/deprecate")]
    public async Task Deprecate(Guid workspaceRef, Guid refId, CancellationToken ct)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsDelete, ct);
        await _service.DeprecateAsync(workspaceId, refId, ct);
    }

    [HttpPost("{refId:guid}/runs")]
    public async Task<StartRunResponse> StartManualRun(Guid workspaceRef, Guid refId, [FromBody] StartRunRequest request, CancellationToken ct)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsUpdate, ct);

        // Validates the workflow belongs to the workspace before delegating to the engine.
        await _service.GetCurrentAsync(workspaceId, refId, ct);
        return await _engineClient.StartManualRunAsync(refId, request, ct);
    }
}

