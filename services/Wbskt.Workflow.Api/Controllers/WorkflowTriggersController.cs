using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public class WorkflowTriggersController : ControllerBase
{
    private readonly IWorkflowEngine _workflowEngine;

    public WorkflowTriggersController(IWorkflowEngine workflowEngine)
    {
        _workflowEngine = workflowEngine;
    }

    [HttpPost("{refId}/trigger")]
    public IActionResult TriggerWorkflow(Guid refId, [FromBody] Dictionary<string, object> initialData)
    {
        // Fire and forget — don’t wait for completion
        var context = new WorkflowContext(Guid.NewGuid(), initialData);
        _ = _workflowEngine.ExecuteWorkflowAsync(refId, context);

        return Accepted(); // HTTP 202 - process started
    }
}
