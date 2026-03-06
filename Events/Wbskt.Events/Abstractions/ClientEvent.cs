namespace Wbskt.Events.Abstractions;

public abstract record ClientEvent(Guid ClientRefId, int WorkspaceId) : WorkspaceEvent(WorkspaceId);