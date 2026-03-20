using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientRegisteredEvent")]
public sealed record ClientRegisteredEvent(Guid ClientRefId, int WorkspaceId, Guid PolicyRefId, string Name) : ClientEvent(ClientRefId, WorkspaceId);
