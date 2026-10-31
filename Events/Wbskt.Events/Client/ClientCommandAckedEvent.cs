using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

// Transport-level delivery ack: the SDK auto-sends sys.ack when it receives a command frame
// carrying a commandId. App-level acks (with status/result data) are ordinary client messages.
[DeviceTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandAckedEvent", Scope = NotifyScope.Client)]
public sealed record ClientCommandAckedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, Guid CommandId) : BaseEvent, IClientContext;
