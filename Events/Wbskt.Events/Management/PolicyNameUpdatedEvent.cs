using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

public sealed record PolicyNameUpdatedEvent(Guid PolicyRefId, int Workspace, string NewName, string OldName)
    : RegistrationPolicyEvent(PolicyRefId, Workspace);
