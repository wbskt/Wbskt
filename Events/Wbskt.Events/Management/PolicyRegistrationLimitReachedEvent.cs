using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnPolicyRegistrationLimitReachedEvent")]
public sealed record PolicyRegistrationLimitReachedEvent(Guid PolicyRefId, int WorkspaceId, int Limit) : RegistrationPolicyEvent(PolicyRefId, WorkspaceId);
