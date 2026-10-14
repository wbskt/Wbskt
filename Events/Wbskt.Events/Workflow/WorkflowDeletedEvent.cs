using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

/// <summary>A workflow was deleted; <c>WorkflowId</c> is its newest version, and its active runs are being cancelled.</summary>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnWorkflowDeletedEvent")]
public sealed record WorkflowDeletedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, int CancelledRuns)
    : ActorEvent, IWorkflowContext;
