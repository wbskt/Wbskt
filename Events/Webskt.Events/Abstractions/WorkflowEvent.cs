namespace Webskt.Events.Abstractions;

public abstract record WorkflowEvent(Guid WorkflowRefId, int WorkspaceId) : WorkspaceEvent(WorkspaceId);