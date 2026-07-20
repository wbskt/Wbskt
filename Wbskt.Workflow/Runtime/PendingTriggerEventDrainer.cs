using System.Text.Json;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class PendingTriggerEventDrainer : IPendingTriggerEventDrainer
{
    private readonly IPendingTriggerEventProvider _pendingTriggerEventProvider;
    private readonly IInboundHub _inboundHub;

    public PendingTriggerEventDrainer(IPendingTriggerEventProvider pendingTriggerEventProvider, IInboundHub inboundHub)
    {
        _pendingTriggerEventProvider = pendingTriggerEventProvider;
        _inboundHub = inboundHub;
    }

    // Drains exactly ONE queued event per run-terminal (design §4.6). BookmarkResumer/RunFinalizer
    // call DrainAsync once whenever a run frees up its correlation slot; draining more than one here
    // would re-enter the concurrency enforcer while the just-started run is still active and just
    // re-enqueue everything it dequeues, looping forever.
    public async Task DrainAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
    {
        var row = await _pendingTriggerEventProvider.DequeueNextAsync(workflowRefId, triggerNodeId, correlationKey, ct);
        if (row is null)
        {
            return;
        }

        InboundEvent? evt = JsonSerializer.Deserialize<InboundEvent>(row.InboundEventJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (evt is null)
        {
            throw new InvalidOperationException("Pending trigger event payload could not be deserialized.");
        }

        // Re-mint the idempotency identity: the original InboundEventId already has a "Succeeded"
        // claim from when the event was first queued, so re-injecting it unchanged would make
        // BookmarkResumer.MatchInboundAsync treat this dispatch as a duplicate and drop it.
        evt = evt with { InboundEventId = $"{evt.InboundEventId}:drain:{row.Id}" };

        await _inboundHub.HandleAsync(evt, ct);
    }
}
