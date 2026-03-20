using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnPolicyRegistrationAttemptedOnDisabledEvent")]
public sealed record PolicyRegistrationAttemptedOnDisabledEvent(Guid PolicyRefId, int WorkspaceId, string ClientName) : RegistrationPolicyEvent(PolicyRefId, WorkspaceId);
