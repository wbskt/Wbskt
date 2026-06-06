using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workspaces/{workspaceRef:guid}")]
[ApiController]
[Authorize]
public sealed class WorkflowRunsController : ControllerBase
{
    private readonly IWorkflowRunQueryService _runQueryService;
    private readonly IWorkflowEngineClient _engineClient;
    private readonly IAuthServiceClient _authClient;

    public WorkflowRunsController(IWorkflowRunQueryService runQueryService, IWorkflowEngineClient engineClient, IAuthServiceClient authClient)
    {
        _runQueryService = runQueryService;
        _engineClient = engineClient;
        _authClient = authClient;
    }

    [HttpGet("workflows/{workflowRefId:guid}/runs")]
    public async Task<RunListResponse> List(Guid workspaceRef, Guid workflowRefId, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        return await _runQueryService.ListByWorkflowAsync(workspaceId, workflowRefId, status, top, cursor, ct);
    }

    [HttpGet("runs/{runRefId:guid}")]
    public async Task<RunDetailDto> Get(Guid workspaceRef, Guid runRefId, CancellationToken ct)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsRead, ct);
        return await _runQueryService.GetDetailAsync(workspaceId, runRefId, ct);
    }

    [HttpPost("runs/{runRefId:guid}/cancel")]
    public async Task Cancel(Guid workspaceRef, Guid runRefId, [FromBody] CancelRunRequest req, CancellationToken ct)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsUpdate, ct);
        await _runQueryService.CancelAsync(workspaceId, runRefId, req.Reason, ct);
    }

    [HttpPost("runs/{runRefId:guid}/signals/{signalName}")]
    public async Task<SignalResponse> Signal(Guid workspaceRef, Guid runRefId, string signalName, [FromBody] SignalRequest req, CancellationToken ct)
    {
        int workspaceId = await _authClient.ResolveWorkspaceAsync(workspaceRef, Permissions.WorkflowsUpdate, ct);
        await _runQueryService.EnsureRunInWorkspaceAsync(workspaceId, runRefId, ct);
        return await _engineClient.SignalAsync(runRefId, signalName, req, ct);
    }
}

