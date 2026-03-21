using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnNodeExecutionCompletedEvent")]
public sealed record NodeExecutionCompletedEvent(
    int WorkspaceId,
    Guid WorkflowRefId,
    int WorkflowId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    List<string> ActivatedPorts) : BaseEvent, IWorkflowContext;