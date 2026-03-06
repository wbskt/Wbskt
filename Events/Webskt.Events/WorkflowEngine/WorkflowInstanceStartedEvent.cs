using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowInstanceStartedEvent(
    Guid InstanceId, 
    Guid WorkflowRefId, 
    int WorkspaceId
) : WorkspaceEvent(WorkspaceId);