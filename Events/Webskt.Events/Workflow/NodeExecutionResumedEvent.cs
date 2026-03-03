using Webskt.Events.Abstractions;

namespace Webskt.Events.Workflow;

public record NodeExecutionResumedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId
) : WorkspaceEvent(WorkspaceId);
