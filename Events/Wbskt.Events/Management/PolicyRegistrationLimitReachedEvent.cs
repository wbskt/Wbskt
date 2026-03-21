using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnPolicyRegistrationLimitReachedEvent")]
public sealed record PolicyRegistrationLimitReachedEvent(Guid PolicyRefId, int PolicyId, int WorkspaceId, int Limit) : BaseEvent, IPolicyContext;
