using Webskt.Events.Abstractions;

namespace Webskt.Events.WorkflowEngine;

public record WorkflowInstanceStartedEvent(
    Guid InstanceId, 
    Guid WorkflowRefId, 
    int WorkspaceId
) : WorkspaceEvent(WorkspaceId);