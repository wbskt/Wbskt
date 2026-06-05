using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/manual")]
public sealed class InboundManualController(IInboundHub hub, IRunProvider runProvider) : ControllerBase
{
    [HttpPost("{workflowRefId:guid}")]
    public async Task<InboundManualResponse> Post(Guid workflowRefId, [FromBody] JsonElement payload, CancellationToken ct)
    {
        InboundEvent evt = new(
            "manual",
            $"manual:{workflowRefId}",
            $"manual:{workflowRefId}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["workflowDefinitionRefId"] = JsonSerializer.SerializeToElement(workflowRefId.ToString()),
                ["body"] = payload
            },
            default);

        TriggerDispatchResult result = await hub.HandleAsync(evt, ct);

        if (result.RunId.HasValue)
        {
            RunRow run = await runProvider.GetByIdAsync(result.RunId.Value, ct);
            return new InboundManualResponse(result.Outcome.ToString(), run.RefId, result.RunId);
        }

        return new InboundManualResponse(result.Outcome.ToString(), null, null);
    }
}

public sealed record InboundManualResponse(string Outcome, Guid? RunRefId, long? RunId);
