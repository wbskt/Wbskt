using Webskt.Events.Abstractions;

namespace Webskt.Events.Workflow;

public record NodeExecutionCompletedEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    List<string> ActivatedPorts
) : WorkspaceEvent(WorkspaceId);