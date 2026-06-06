using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A workflow run reached a non-failure terminal status (Succeeded or Cancelled). Surfaced in the
/// central Events Log as an Info milestone; the detailed per-node trace stays in HistoryEvents.
/// </summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowRunCompletedEvent")]
public sealed record WorkflowRunCompletedEvent(Guid WorkflowRefId, int WorkflowId, Guid RunRefId, int WorkspaceId, string Status)
    : BaseEvent, IWorkflowContext;
