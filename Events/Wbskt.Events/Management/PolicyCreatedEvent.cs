using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnPolicyCreatedEvent")]
public sealed record PolicyCreatedEvent(Guid PolicyRefId, int PolicyId, int WorkspaceId, string Name) : ActorEvent, IPolicyContext;
