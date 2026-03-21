using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnNodeExecutionWaitingEvent")]
public sealed record NodeExecutionWaitingEvent(
    int WorkspaceId,
    Guid WorkflowRefId,
    int WorkflowId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    DateTime WaitUntil) : BaseEvent, IWorkflowContext;
