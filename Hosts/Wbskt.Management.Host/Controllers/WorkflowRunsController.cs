using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Management.Host.Controllers;

[Route("api")]
[ApiController]
public sealed class WorkflowRunsController : ControllerBase
{
    private readonly IWorkflowRunQueryService _runQueryService;
    private readonly IInboundHub? _inboundHub;

    public WorkflowRunsController(IWorkflowRunQueryService runQueryService)
    {
        _runQueryService = runQueryService;
    }

    public WorkflowRunsController(IWorkflowRunQueryService runQueryService, IInboundHub inboundHub)
    {
        _runQueryService = runQueryService;
        _inboundHub = inboundHub;
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
        _ = _inboundHub ?? throw new InvalidOperationException("Inbound hub is not configured.");

        var payload = new Dictionary<string, JsonElement>
        {
            ["body"] = req.Payload
        };
        TriggerDispatchResult result = await _inboundHub.HandleAsync(new InboundEvent(
            "signal",
            $"{runRefId}:{signalName}",
            $"signal:{runRefId}:{signalName}:{Guid.NewGuid()}",
            payload,
            DateTime.UtcNow), ct);
        bool matched = result.Outcome != TriggerDispatchOutcome.NoRegistration;
        return new SignalResponse(matched, result.Outcome.ToString());
    }
}
