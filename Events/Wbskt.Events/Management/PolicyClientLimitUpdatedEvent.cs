using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnPolicyClientLimitUpdatedEvent")]
public sealed record PolicyClientLimitUpdatedEvent(Guid PolicyRefId, int PolicyId, int WorkspaceId, int? NewLimit, int? OldLimit) : ActorEvent, IPolicyContext;
