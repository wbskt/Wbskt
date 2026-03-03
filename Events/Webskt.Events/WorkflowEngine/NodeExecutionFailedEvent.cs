using Webskt.Events.Abstractions;

namespace Webskt.Events.WorkflowEngine;

public record NodeExecutionFailedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    string ErrorMessage
) : WorkspaceEvent(WorkspaceId);