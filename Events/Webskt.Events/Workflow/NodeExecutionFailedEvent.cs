using Webskt.Events.Abstractions;

namespace Webskt.Events.Workflow;

public record NodeExecutionFailedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    string ErrorMessage
) : WorkspaceEvent(WorkspaceId);