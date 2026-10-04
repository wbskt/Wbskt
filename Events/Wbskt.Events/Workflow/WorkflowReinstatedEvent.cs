using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowReinstatedEvent")]
public sealed record WorkflowReinstatedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, int Version)
    : ActorEvent, IWorkflowContext;
