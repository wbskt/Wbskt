namespace Wbskt.Events.Abstractions;

public abstract record RegistrationPolicyEvent(Guid PolicyRefId, int WorkspaceId) : WorkspaceEvent(WorkspaceId);