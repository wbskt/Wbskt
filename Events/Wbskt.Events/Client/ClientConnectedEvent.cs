using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientConnectedEvent")]
public sealed record ClientConnectedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string HostId) : BaseEvent, IClientContext;
