using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowInstanceStartedEvent(
    Guid InstanceId,
    Guid WorkflowRefId,
    int WorkflowId,
    int WorkspaceId) : BaseEvent, IWorkflowContext;