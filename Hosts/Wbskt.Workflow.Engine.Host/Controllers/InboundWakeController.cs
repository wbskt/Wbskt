using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/wake")]
public sealed class InboundWakeController(IInboundHub hub) : ControllerBase
{
    [HttpPost("{token}")]
    public async Task<InboundWakeResponse> Post(string token, [FromBody] JsonElement payload, CancellationToken ct)
    {
        // CorrelationKeyResolver maps the "http-wake" channel to "http-wake:{wakeToken}", which
        // must match the bookmark minted by WaitForHttpNodeExecutor for this run/token.
        InboundEvent inboundEvent = new(
            "http-wake",
            $"http-wake:{token}",
            $"http-wake:{token}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["wakeToken"] = JsonSerializer.SerializeToElement(token),
                ["body"] = payload
            },
            default);

        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, ct);
        return new InboundWakeResponse(result.Outcome.ToString(), result.Outcome == TriggerDispatchOutcome.ResumedBookmark);
    }
}

public sealed record InboundWakeResponse(string Outcome, bool Matched);
