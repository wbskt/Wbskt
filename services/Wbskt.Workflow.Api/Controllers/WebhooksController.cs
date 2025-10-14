using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Common.Readers;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IWorkflowsReader _workflowsReader;
    private readonly IWorkflowEngine _workflowEngine;

    public WebhooksController(IWorkflowsReader workflowsReader, IWorkflowEngine workflowEngine)
    {
        _workflowsReader = workflowsReader;
        _workflowEngine = workflowEngine;
    }

    [HttpPost("{webhookId:guid}")]
    public async Task<IActionResult> TriggerWebhook(Guid webhookId, [FromBody] JsonElement body)
    {
        var workflow = await _workflowsReader.GetByWebhookIdAsync(webhookId, CancellationToken.None);
        if (workflow == null)
        {
            return NotFound();
        }

        var initialData = new Dictionary<string, object>
        {
            ["trigger_type"] = "Webhook",
            ["body"] = body,
            ["headers"] = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
            ["query"] = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString())
        };
        var context = new WorkflowContext(Guid.NewGuid(), initialData);

        _ = _workflowEngine.ExecuteWorkflowAsync(workflow.RefId, context);

        return Accepted();
    }
}
