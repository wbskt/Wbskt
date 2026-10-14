using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Workflow;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowReinstatedEvent")]
public sealed record WorkflowReinstatedEvent(Guid WorkflowRefId, int WorkflowId, int WorkspaceId, int Version)
    : ActorEvent, IWorkflowContext;
