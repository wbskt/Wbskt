using Webskt.Events.Abstractions;

namespace Webskt.Events.WorkflowEngine;

public record WorkflowInstanceCompletedEvent(
    Guid InstanceId, 
    Guid WorkflowRefId, 
    int WorkspaceId,
    string Status
) : WorkspaceEvent(WorkspaceId);