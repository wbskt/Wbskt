using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

/// <summary>
/// A workflow was disabled: <c>Version</c> (definition <c>WorkflowId</c>) was live until now. Events queued
/// behind a busy run were dropped, and the runs going were cancelled when that was asked for.
/// </summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowDisabledEvent")]
public sealed record WorkflowDisabledEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, int Version, int CancelledRuns, int DroppedQueuedEvents)
    : ActorEvent, IWorkflowContext;
