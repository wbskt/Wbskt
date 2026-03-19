using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

public sealed record PolicyClientLimitUpdatedEvent(Guid PolicyRefId, int Workspace, int? NewLimit, int? OldLimit)
    : RegistrationPolicyEvent(PolicyRefId, Workspace);
