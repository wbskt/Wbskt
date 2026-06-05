using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workflows")]
[ApiController]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IWorkflowDefinitionService _service;
    private readonly IWorkflowEngineClient _engineClient;

    public WorkflowsController(IWorkflowDefinitionService service, IWorkflowEngineClient engineClient)
    {
        _service = service;
        _engineClient = engineClient;
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
        await _service.GetCurrentAsync(refId, ct);
        return await _engineClient.StartManualRunAsync(refId, request, ct);
    }
}

