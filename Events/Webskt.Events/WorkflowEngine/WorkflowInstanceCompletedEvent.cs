using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Info)]
public sealed record WorkflowInstanceCompletedEvent(
    Guid InstanceId, 
    Guid WorkflowRefId, 
    int WorkspaceId,
    string Status
) : WorkspaceEvent(WorkspaceId);