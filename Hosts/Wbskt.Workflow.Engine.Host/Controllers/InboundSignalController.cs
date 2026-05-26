using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/signal")]
public sealed class InboundSignalController(IInboundHub hub) : ControllerBase
{
    [HttpPost("{correlationKey}")]
    public async Task<InboundSignalResponse> Post(string correlationKey, [FromBody] JsonElement payload, CancellationToken ct)
    {
        InboundEvent inboundEvent = new(
            "signal",
            correlationKey,
            $"signal:{correlationKey}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["body"] = payload
            },
            default);

        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, ct);
        return new InboundSignalResponse(result.Outcome.ToString(), result.Outcome == TriggerDispatchOutcome.ResumedBookmark);
    }
}

public sealed record InboundSignalResponse(string Outcome, bool Matched);
