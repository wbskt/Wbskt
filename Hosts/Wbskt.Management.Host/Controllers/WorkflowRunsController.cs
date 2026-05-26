using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Controllers;

[Route("api")]
[ApiController]
public sealed class WorkflowRunsController(IWorkflowRunQueryService runQueryService) : ControllerBase
{
    [HttpGet("workflows/{workflowRefId:guid}/runs")]
    public async Task<RunListResponse> List(Guid workflowRefId, [FromQuery] string? status, [FromQuery] int top = 50, [FromQuery] long? cursor = null, CancellationToken ct = default)
    {
        return await runQueryService.ListByWorkflowAsync(workflowRefId, status, top, cursor, ct);
    }

    [HttpGet("runs/{runRefId:guid}")]
    public async Task<RunDetailDto> Get(Guid runRefId, CancellationToken ct)
    {
        return await runQueryService.GetDetailAsync(runRefId, ct);
    }

    [HttpPost("runs/{runRefId:guid}/cancel")]
    public async Task Cancel(Guid runRefId, [FromBody] CancelRunRequest req, CancellationToken ct)
    {
        await runQueryService.CancelAsync(runRefId, req.Reason, ct);
    }
}
