using Webskt.Events.Abstractions;

namespace Webskt.Events.Workflow;

public record NodeExecutionStartedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    string NodeType,
    string NodeName
) : WorkspaceEvent(WorkspaceId);

public record NodeExecutionCompletedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    List<string> ActivatedPorts
) : WorkspaceEvent(WorkspaceId);

public record NodeExecutionFailedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    string ErrorMessage
) : WorkspaceEvent(WorkspaceId);
