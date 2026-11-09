using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A plan limit refused triggers of this workflow: the events were thrown away and no run started. One per workflow per window, however many were refused in it, so a burst at
/// the ceiling is one warning rather than hundreds.
/// </summary>
/// <param name="RefusedCount">How many triggers were refused between the first and last time.</param>
/// <param name="TriggerKinds">The channels they came in on, comma-separated: client, webhook, schedule…</param>
/// <param name="Ceiling">The limit that refused them: the plan's active runs, all going at each refusal, or
/// its credits for the month (whole credits), all spent; 0 for a workspace's own credit cap.</param>
/// <param name="Reason">Which limit it was.</param>
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
    int Ceiling,
    RunRefusalReason Reason = RunRefusalReason.ActiveRunLimit) : BaseEvent, IWorkflowContext;
