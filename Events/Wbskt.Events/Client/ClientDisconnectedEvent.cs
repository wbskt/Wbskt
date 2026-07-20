using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientDisconnectedEvent")]
public sealed record ClientDisconnectedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Reason, string HostId) : BaseEvent, IClientContext;
