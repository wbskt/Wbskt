namespace Wbskt.Events.Abstractions;

public abstract record WorkflowEvent(Guid WorkflowRefId, int WorkspaceId) : WorkspaceEvent(WorkspaceId);