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
    /// <summary>The header a webhook caller presents its trigger's shared secret in.</summary>
    public const string SecretHeader = "X-Wbskt-Secret";

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
            default)
        {
            // Carried beside the payload, never inside it: the payload is persisted as the run's trigger
            // data, and a secret written there would be readable from the run's history forever.
            Secret = Request.Headers.TryGetValue(SecretHeader, out Microsoft.Extensions.Primitives.StringValues presented)
                ? presented.ToString()
                : null
        };

        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, ct);

        // One path can match several registrations, so every outcome is reported. Outcome/RunId
        // summarise the first started run and stay for callers written against the single-run shape.
        IReadOnlyList<InboundDispatchEntry> dispatches = await InboundDispatchProjection.ProjectAsync(result, runProvider, ct);

        Guid? runRefId = null;
        if (result.RunId.HasValue)
        {
            runRefId = dispatches.FirstOrDefault(d => d.RunId == result.RunId)?.RunRefId
                ?? (await runProvider.GetByIdAsync(result.RunId.Value, ct)).RefId;
        }

        logger?.LogInformation("Webhook request for channel {ChannelKind} resulted in outcome {Outcome} across {Count} registration(s) with RunRefId {RunRefId}", channelKind, result.Outcome, dispatches.Count, runRefId);
        return new InboundWebhookResponse(result.Outcome.ToString(), runRefId, dispatches);
    }
}

public sealed record InboundWebhookResponse(string Outcome, Guid? RunId, IReadOnlyList<InboundDispatchEntry> Registrations);
