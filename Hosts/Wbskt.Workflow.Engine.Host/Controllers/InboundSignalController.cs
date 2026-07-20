using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Middleware;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/signal")]
[InboundEndpoint]
public sealed class InboundSignalController(IInboundHub hub, ILogger<InboundSignalController>? logger = null) : ControllerBase
{
    [HttpPost("{scopeRunRefId:guid}/{signalName}")]
    public async Task<InboundSignalResponse> Post(Guid scopeRunRefId, string signalName, [FromBody] JsonElement payload, CancellationToken ct)
    {
        logger?.LogInformation("Received signal request {SignalName} for scope {ScopeRunRefId}", signalName, scopeRunRefId);
        // CorrelationKeyResolver maps the "signal" channel to "signal:{signalName}:{scopeRunRefId}",
        // which must match the bookmark minted by AwaitSignalNodeExecutor for this run.
        InboundEvent inboundEvent = new(
            "signal",
            [$"signal:{signalName}:{scopeRunRefId}"],
            $"signal:{scopeRunRefId}:{signalName}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["signalName"] = JsonSerializer.SerializeToElement(signalName),
                ["scopeRunRefId"] = JsonSerializer.SerializeToElement(scopeRunRefId.ToString()),
                ["body"] = payload
            },
            default);

        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, ct);
        logger?.LogInformation("Signal request {SignalName} for scope {ScopeRunRefId} resulted in outcome {Outcome}", signalName, scopeRunRefId, result.Outcome);
        return new InboundSignalResponse(result.Outcome.ToString(), result.Outcome == TriggerDispatchOutcome.ResumedBookmark);
    }
}

public sealed record InboundSignalResponse(string Outcome, bool Matched);
