using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Controllers;

[Route("api")]
[ApiController]
public sealed class WorkflowRunsController : ControllerBase
{
    private readonly IWorkflowRunQueryService _runQueryService;
    private readonly IWorkflowEngineClient _engineClient;

    public WorkflowRunsController(IWorkflowRunQueryService runQueryService, IWorkflowEngineClient engineClient)
    {
        _runQueryService = runQueryService;
        _engineClient = engineClient;
    }

    [HttpGet("workflows/{workflowRefId:guid}/runs")]
    public async Task<RunListResponse> List(Guid workflowRefId, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default)
    {
        return await _runQueryService.ListByWorkflowAsync(workflowRefId, status, top, cursor, ct);
    }

    [HttpGet("runs/{runRefId:guid}")]
    public async Task<RunDetailDto> Get(Guid runRefId, CancellationToken ct)
    {
        return await _runQueryService.GetDetailAsync(runRefId, ct);
    }

    [HttpPost("runs/{runRefId:guid}/cancel")]
    public async Task Cancel(Guid runRefId, [FromBody] CancelRunRequest req, CancellationToken ct)
    {
        await _runQueryService.CancelAsync(runRefId, req.Reason, ct);
    }

    [HttpPost("runs/{runRefId:guid}/signals/{signalName}")]
    public async Task<SignalResponse> Signal(Guid runRefId, string signalName, [FromBody] SignalRequest req, CancellationToken ct)
    {
        return await _engineClient.SignalAsync(runRefId, signalName, req, ct);
    }
}

