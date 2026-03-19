using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnPolicyDisabledEvent")]
public sealed record PolicyDisabledEvent(Guid PolicyRefId, int WorkspaceId) : RegistrationPolicyEvent(PolicyRefId, WorkspaceId);
