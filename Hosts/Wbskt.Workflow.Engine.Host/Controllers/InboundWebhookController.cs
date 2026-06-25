using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/webhook")]
public sealed class InboundWebhookController(IInboundHub hub, Wbskt.Workflow.Abstraction.Providers.IRunProvider runProvider, ILogger<InboundWebhookController>? logger = null) : ControllerBase
{
    [HttpPost("{channelKind}")]
    public async Task<InboundWebhookResponse> Post(string channelKind, [FromBody] JsonElement payload, CancellationToken ct)
    {
        logger?.LogInformation("Received webhook request for channel {ChannelKind}", channelKind);
        InboundEvent inboundEvent = new(
            "webhook",
            [$"webhook:{channelKind}"],
            $"webhook:{channelKind}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["webhookPath"] = JsonSerializer.SerializeToElement(channelKind),
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
        
        logger?.LogInformation("Webhook request for channel {ChannelKind} resulted in outcome {Outcome} with RunRefId {RunRefId}", channelKind, result.Outcome, runRefId);
        return new InboundWebhookResponse(result.Outcome.ToString(), runRefId);
    }
}

public sealed record InboundWebhookResponse(string Outcome, Guid? RunId);
