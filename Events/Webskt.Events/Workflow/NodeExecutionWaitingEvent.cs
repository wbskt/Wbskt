using Webskt.Events.Abstractions;

namespace Webskt.Events.Workflow;

public record NodeExecutionWaitingEvent(
    int WorkspaceId,
    Guid InstanceId,
    Guid PointerId,
    Guid NodeId,
    DateTime WaitUntil
) : WorkspaceEvent(WorkspaceId);
