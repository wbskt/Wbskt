using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.WorkflowEngine;

[EventCriticality(EventCriticality.Error)]
public sealed record NodeExecutionFailedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    string ErrorMessage
) : WorkspaceEvent(WorkspaceId);