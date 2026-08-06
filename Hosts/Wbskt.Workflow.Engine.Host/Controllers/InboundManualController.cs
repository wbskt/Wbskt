using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Middleware;

namespace Wbskt.Workflow.Engine.Host.Controllers;

[ApiController]
[Route("api/inbound/manual")]
[InboundEndpoint]
public sealed class InboundManualController(IInboundHub hub, IRunProvider runProvider) : ControllerBase
{
    [HttpPost("{workflowRefId:guid}")]
    public async Task<InboundManualResponse> Post(Guid workflowRefId, [FromBody] JsonElement payload, [FromQuery] string? idempotencyKey, CancellationToken ct)
    {
        // A caller-supplied idempotency key makes the inbound event id stable across retries,
        // so re-POSTing the same key dedupes to a single run instead of starting another.
        string inboundEventId = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"manual:{workflowRefId}:{Guid.NewGuid()}"
            : $"manual:{workflowRefId}:{idempotencyKey}";

        InboundEvent evt = new(
            "manual",
            [$"manual:{workflowRefId}"],
            inboundEventId,
            new Dictionary<string, JsonElement>
            {
                ["workflowDefinitionRefId"] = JsonSerializer.SerializeToElement(workflowRefId.ToString()),
                ["body"] = payload
            },
            default);

        TriggerDispatchResult result = await hub.HandleAsync(evt, ct);

        // A workflow may carry more than one manual trigger, so the event can start more than one run.
        // Outcome/RunRefId/RunId summarise the first; Registrations carries all of them.
        IReadOnlyList<InboundDispatchEntry> dispatches = await InboundDispatchProjection.ProjectAsync(result, runProvider, ct);

        if (result.RunId.HasValue)
        {
            Guid runRefId = dispatches.FirstOrDefault(d => d.RunId == result.RunId)?.RunRefId
                ?? (await runProvider.GetByIdAsync(result.RunId.Value, ct)).RefId;
            return new InboundManualResponse(result.Outcome.ToString(), runRefId, result.RunId, dispatches);
        }

        return new InboundManualResponse(result.Outcome.ToString(), null, null, dispatches);
    }
}

public sealed record InboundManualResponse(string Outcome, Guid? RunRefId, long? RunId, IReadOnlyList<InboundDispatchEntry> Registrations);
