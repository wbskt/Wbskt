using Webskt.Events.Abstractions;

namespace Webskt.Events.Workflow;

public record WorkflowInstanceStartedEvent(
    Guid InstanceId, 
    Guid WorkflowRefId, 
    int WorkspaceId
) : WorkspaceEvent(WorkspaceId);

public record WorkflowInstanceCompletedEvent(
    Guid InstanceId, 
    Guid WorkflowRefId, 
    int WorkspaceId,
    string Status
) : WorkspaceEvent(WorkspaceId);
