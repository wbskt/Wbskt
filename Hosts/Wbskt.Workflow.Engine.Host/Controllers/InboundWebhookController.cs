using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/webhook")]
public sealed class InboundWebhookController(IInboundHub hub, Wbskt.Workflow.Abstraction.Providers.IRunProvider runProvider) : ControllerBase
{
    [HttpPost("{channelKind}")]
    public async Task<InboundWebhookResponse> Post(string channelKind, [FromBody] JsonElement payload, CancellationToken ct)
    {
        InboundEvent inboundEvent = new(
            "webhook",
            [$"webhook:{channelKind}"],
            $"webhook:{channelKind}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["body"] = payload
            },
            default);

        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, ct);
        
        Guid? runRefId = null;
        if (result.RunId.HasValue)
        {
            var run = await runProvider.GetByIdAsync(result.RunId.Value, ct);
            runRefId = run.RefId;
        }
        
        return new InboundWebhookResponse(result.Outcome.ToString(), runRefId);
    }
}

public sealed record InboundWebhookResponse(string Outcome, Guid? RunId);
