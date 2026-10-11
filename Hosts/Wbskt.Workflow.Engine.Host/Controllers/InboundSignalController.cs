using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Engine.Host.Middleware;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route(EngineInboundApi.SignalRoute)]
[InboundEndpoint]
public sealed class InboundSignalController(IInboundHub hub, ILogger<InboundSignalController>? logger = null) : ControllerBase
{
    [HttpPost("{scopeRunRefId:guid}/{signalName}")]
    public async Task<InboundResponse> Post(Guid scopeRunRefId, string signalName, [FromBody] JsonElement payload, CancellationToken ct)
    {
        logger?.LogInformation("Received signal request {SignalName} for scope {ScopeRunRefId}", signalName, scopeRunRefId);
        InboundEvent inboundEvent = SignalChannel.Event(scopeRunRefId, signalName, payload);

        // The signal channel only ever resolves bookmarks - nothing publishes a trigger registration on
        // it - so a signal cannot fan out and the response's Registrations is always empty.
        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, ct);
        logger?.LogInformation("Signal request {SignalName} for scope {ScopeRunRefId} resulted in outcome {Outcome}", signalName, scopeRunRefId, result.Outcome);
        return InboundDispatchProjection.ToResponse(result);
    }
}
