using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class PendingTriggerEventDrainer : IPendingTriggerEventDrainer
{
    private readonly IPendingTriggerEventProvider _pendingTriggerEventProvider;
    private readonly IInboundHub _inboundHub;
    private readonly int _leaseSeconds;
    private readonly ILogger<PendingTriggerEventDrainer>? _logger;

    public PendingTriggerEventDrainer(
        IPendingTriggerEventProvider pendingTriggerEventProvider,
        IInboundHub inboundHub,
        IOptions<WorkflowEngineOptions>? options = null,
        ILogger<PendingTriggerEventDrainer>? logger = null)
    {
        _pendingTriggerEventProvider = pendingTriggerEventProvider;
        _inboundHub = inboundHub;
        _leaseSeconds = (options?.Value ?? new WorkflowEngineOptions()).LeaseDurationSeconds;
        _logger = logger;
    }

    // Drains exactly ONE queued event per run-terminal (design §4.6). RunFinalizer calls DrainAsync once
    // whenever a run frees up its correlation slot, and the backlog reaper for a queue no run will drain;
    // draining more than one here would re-enter the concurrency enforcer while the just-started run is
    // still active and just re-enqueue everything it dequeues, looping forever.
    public async Task DrainAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
    {
        PendingTriggerEventRow? row = await _pendingTriggerEventProvider.LeaseNextAsync(workflowRefId, triggerNodeId, correlationKey, _leaseSeconds, ct);
        if (row is null)
        {
            return;
        }

        InboundEvent? evt = Deserialize(row);
        if (evt is null)
        {
            // It can never be replayed; kept, it would hold the head of its queue until the TTL.
            _logger?.LogError("Queued trigger event {EventId} could not be read; dropping it so the events behind it can start.", row.Id);
            await _pendingTriggerEventProvider.DeleteByIdAsync(row.Id, CancellationToken.None);
            return;
        }

        // Re-mint the idempotency identity: the original InboundEventId already has a "Succeeded"
        // claim from when the event was first queued, so re-injecting it unchanged would make
        // BookmarkResumer.MatchInboundAsync treat this dispatch as a duplicate and drop it. The new id
        // is stable per row, so a replay retried after a crash past its start is dropped as a duplicate.
        // ReplayFor keeps the replay on the trigger that queued it: the original arrival already started
        // or queued every other registration on its match keys, and those must not see it twice. It also
        // starts the replay past the plan's active-run ceiling: the run that just finished freed the
        // place it takes.
        evt = evt with
        {
            InboundEventId = $"{evt.InboundEventId}:drain:{row.Id}",
            ReplayFor = new QueuedTrigger(row.WorkflowRefId, row.TriggerNodeId, row.Id)
        };

        // A throw leaves the row leased: once the lease runs out the next finalizer on the key, or the
        // backlog reaper, replays it again rather than losing it (#962).
        await _inboundHub.HandleAsync(evt, ct);
        await _pendingTriggerEventProvider.DeleteByIdAsync(row.Id, CancellationToken.None);
    }

    private static InboundEvent? Deserialize(PendingTriggerEventRow row)
    {
        try
        {
            return JsonSerializer.Deserialize<InboundEvent>(row.InboundEventJson, EngineJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
