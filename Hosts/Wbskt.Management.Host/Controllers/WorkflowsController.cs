using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workflows")]
[ApiController]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IWorkflowDefinitionService _service;
    private readonly IInboundHub? _inboundHub;
    private readonly IRunProvider? _runProvider;

    public WorkflowsController(IWorkflowDefinitionService service, IInboundHub inboundHub, IRunProvider runProvider)
    {
        _service = service;
        _inboundHub = inboundHub;
        _runProvider = runProvider;
    }

    [HttpPost]
    public async Task<WorkflowPublishResponse> Publish([FromBody] WorkflowPublishRequest request, CancellationToken ct)
    {
        return await _service.PublishAsync(request, ct);
    }

    [HttpGet("{refId:guid}")]
    public async Task<WorkflowDefinitionDto> GetCurrent(Guid refId, CancellationToken ct)
    {
        return await _service.GetCurrentAsync(refId, ct);
    }

    [HttpGet("{refId:guid}/versions/{version:int}")]
    public async Task<WorkflowDefinitionDto> GetVersion(Guid refId, int version, CancellationToken ct)
    {
        return await _service.GetVersionAsync(refId, version, ct);
    }

    [HttpPost("{refId:guid}/deprecate")]
    public async Task Deprecate(Guid refId, CancellationToken ct)
    {
        await _service.DeprecateAsync(refId, ct);
    }

    [HttpPost("{refId:guid}/runs")]
    public async Task<StartRunResponse> StartManualRun(Guid refId, [FromBody] StartRunRequest request, CancellationToken ct)
    {
        _ = _inboundHub ?? throw new InvalidOperationException("Inbound hub is not configured.");
        _ = _runProvider ?? throw new InvalidOperationException("Run provider is not configured.");

        await _service.GetCurrentAsync(refId, ct);
        TriggerDispatchResult result = await _inboundHub.HandleAsync(new InboundEvent(
            "manual",
            request.TriggerNodeId,
            $"manual:{refId}:{Guid.NewGuid()}",
            request.Payload ?? new Dictionary<string, JsonElement>(),
            DateTime.UtcNow), ct);
        if (!result.RunId.HasValue)
        {
            throw new InvalidOperationException("Manual trigger did not start a run.");
        }

        var run = await _runProvider.GetByIdAsync(result.RunId.Value, ct);
        return new StartRunResponse(run.RefId, result.RunId.Value);
    }
}
