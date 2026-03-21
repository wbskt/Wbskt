using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnWorkflowInstanceCompletedEvent")]
public sealed record WorkflowInstanceCompletedEvent(
    Guid InstanceId,
    Guid WorkflowRefId,
    int WorkflowId,
    int WorkspaceId,
    string Status) : BaseEvent, IWorkflowContext;
