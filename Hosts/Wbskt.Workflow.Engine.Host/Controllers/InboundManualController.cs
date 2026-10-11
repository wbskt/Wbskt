using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Engine.Host.Middleware;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route(EngineInboundApi.ManualRoute)]
[InboundEndpoint]
public sealed class InboundManualController(IInboundHub hub) : ControllerBase
{
    [HttpPost("{workflowRefId:guid}")]
    public async Task<InboundResponse> Post(Guid workflowRefId, [FromBody] JsonElement payload, [FromQuery] string? idempotencyKey, CancellationToken ct)
    {
        // A caller-supplied idempotency key makes the inbound event id stable across retries,
        // so re-POSTing the same key dedupes to a single run instead of starting another.
        InboundEvent evt = ManualChannel.Event(workflowRefId, payload, idempotencyKey, default);

        // A workflow may carry more than one manual trigger, so the event can start more than one run.
        TriggerDispatchResult result = await hub.HandleAsync(evt, ct);
        return InboundDispatchProjection.ToResponse(result);
    }
}
