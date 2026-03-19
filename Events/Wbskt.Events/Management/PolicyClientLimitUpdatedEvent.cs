using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnPolicyClientLimitUpdatedEvent")]
public sealed record PolicyClientLimitUpdatedEvent(Guid PolicyRefId, int Workspace, int? NewLimit, int? OldLimit)
    : RegistrationPolicyEvent(PolicyRefId, Workspace);
