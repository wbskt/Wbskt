using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Middleware;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/wake")]
[InboundEndpoint]
public sealed class InboundWakeController(IInboundHub hub) : ControllerBase
{
    [HttpPost("{token}")]
    public async Task<InboundWakeResponse> Post(string token, [FromBody] JsonElement payload, CancellationToken ct)
    {
        InboundEvent inboundEvent = HttpWakeChannel.Event(token, payload);

        // As with the signal endpoint, http-wake only ever resolves bookmarks - no trigger registration
        // is published on this channel - so there is no per-registration fan-out to project.
        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, ct);
        return new InboundWakeResponse(result.Outcome.ToString(), result.Outcome == TriggerDispatchOutcome.ResumedBookmark);
    }
}

public sealed record InboundWakeResponse(string Outcome, bool Matched);
