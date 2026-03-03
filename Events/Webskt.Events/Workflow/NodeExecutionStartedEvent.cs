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