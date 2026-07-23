using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Middleware;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/webhook")]
[InboundEndpoint]
public sealed class InboundWebhookController(IInboundHub hub, Wbskt.Workflow.Abstraction.Providers.IRunProvider runProvider, ILogger<InboundWebhookController>? logger = null) : ControllerBase
{
    [HttpPost("{workspaceRef:guid}/{channelKind}")]
    public async Task<InboundWebhookResponse> Post(Guid workspaceRef, string channelKind, [FromBody] JsonElement payload, CancellationToken ct)
    {
        logger?.LogInformation("Received webhook request for workspace {WorkspaceRef} channel {ChannelKind}", workspaceRef, channelKind);
        // The key is workspace-scoped (webhook:{workspaceRef}:{path}) and must match both the
        // registration minted by TriggerRegistrationService and CorrelationKeyResolver's webhook key.
        InboundEvent inboundEvent = new(
            "webhook",
            [$"webhook:{workspaceRef}:{channelKind}"],
            $"webhook:{workspaceRef}:{channelKind}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["workspaceRefId"] = JsonSerializer.SerializeToElement(workspaceRef.ToString()),
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
