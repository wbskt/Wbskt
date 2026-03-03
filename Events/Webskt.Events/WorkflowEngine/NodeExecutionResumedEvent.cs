using Webskt.Events.Abstractions;

namespace Webskt.Events.WorkflowEngine;

public record NodeExecutionResumedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId
) : WorkspaceEvent(WorkspaceId);
