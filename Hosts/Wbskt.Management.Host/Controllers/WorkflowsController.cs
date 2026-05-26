using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Controllers;

[Route("api/workflows")]
[ApiController]
public sealed class WorkflowsController(IWorkflowDefinitionService service) : ControllerBase
{
    [HttpPost]
    public async Task<WorkflowPublishResponse> Publish([FromBody] WorkflowPublishRequest request, CancellationToken ct)
    {
        return await service.PublishAsync(request, ct);
    }

    [HttpGet("{refId:guid}")]
    public async Task<WorkflowDefinitionDto> GetCurrent(Guid refId, CancellationToken ct)
    {
        return await service.GetCurrentAsync(refId, ct);
    }

    [HttpGet("{refId:guid}/versions/{version:int}")]
    public async Task<WorkflowDefinitionDto> GetVersion(Guid refId, int version, CancellationToken ct)
    {
        return await service.GetVersionAsync(refId, version, ct);
    }

    [HttpPost("{refId:guid}/deprecate")]
    public async Task Deprecate(Guid refId, CancellationToken ct)
    {
        await service.DeprecateAsync(refId, ct);
    }
}
