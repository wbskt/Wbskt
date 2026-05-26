using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/webhook")]
public sealed class InboundWebhookController(IInboundHub hub) : ControllerBase
{
    [HttpPost("{channelKind}")]
    public async Task<InboundWebhookResponse> Post(string channelKind, [FromBody] JsonElement payload, CancellationToken ct)
    {
        InboundEvent inboundEvent = new(
            channelKind,
            $"webhook:{channelKind}",
            $"webhook:{channelKind}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["body"] = payload
            },
            default);

        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, ct);
        return new InboundWebhookResponse(result.Outcome.ToString(), result.RunId);
    }
}

public sealed record InboundWebhookResponse(string Outcome, long? RunId);
