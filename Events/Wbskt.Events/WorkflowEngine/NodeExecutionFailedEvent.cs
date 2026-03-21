using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Error)]
[SignalRNotify("OnNodeExecutionFailedEvent")]
public sealed record NodeExecutionFailedEvent(
    int WorkspaceId,
    Guid WorkflowRefId,
    int WorkflowId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    string ErrorMessage) : BaseEvent, IWorkflowContext;