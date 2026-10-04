using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Workflow;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowDeprecatedEvent")]
public sealed record WorkflowDeprecatedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, int Version)
    : ActorEvent, IWorkflowContext;
