using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

/// <summary>A version of a workflow was published. <c>RestoredFromVersion</c> is set when it was a rollback.</summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowPublishedEvent")]
public sealed record WorkflowPublishedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, int Version, string Name, int? RestoredFromVersion = null)
    : ActorEvent, IWorkflowContext;
