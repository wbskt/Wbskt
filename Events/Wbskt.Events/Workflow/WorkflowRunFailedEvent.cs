using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A workflow run reached a failure terminal status (Failed or PartiallyFailed). Surfaced in the
/// central Events Log as a high-criticality (Error) milestone; the detailed per-node trace stays
/// in HistoryEvents.
/// </summary>
[EventCriticality(EventCriticality.Error)]
[SignalRNotify("OnWorkflowRunFailedEvent")]
public sealed record WorkflowRunFailedEvent(Guid WorkflowRefId, int WorkflowId, Guid RunRefId, int WorkspaceId, string Status)
    : BaseEvent, IWorkflowContext;
