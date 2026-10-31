using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

/// <summary>
/// The workspace's active-run ceiling refused triggers of this workflow: the events were thrown away
/// and no run started. One per workflow per window, however many were refused in it, so a burst at
/// the ceiling is one warning rather than hundreds.
/// </summary>
/// <param name="RefusedCount">How many triggers were refused between the first and last time.</param>
/// <param name="TriggerKinds">The channels they came in on, comma-separated: client, webhook, schedule…</param>
/// <param name="Ceiling">The workspace's limit of active runs; that many were going at each refusal.</param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnWorkflowTriggerRefusedEvent")]
public sealed record WorkflowTriggerRefusedEvent(
    Guid WorkflowRefId,
    int WorkflowId,
    int WorkspaceId,
    int RefusedCount,
    DateTime FirstRefusedAtUtc,
    DateTime LastRefusedAtUtc,
    string TriggerKinds,
    int Ceiling) : BaseEvent, IWorkflowContext;
