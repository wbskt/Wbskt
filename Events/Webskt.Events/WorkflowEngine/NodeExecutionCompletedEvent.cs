using Webskt.Events.Abstractions;

namespace Webskt.Events.WorkflowEngine;

public record NodeExecutionCompletedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    List<string> ActivatedPorts
) : WorkspaceEvent(WorkspaceId);