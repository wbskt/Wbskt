using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

// Transport-level delivery ack: the SDK auto-sends sys.ack when it receives a command frame
// carrying a commandId. App-level acks (with status/result data) are ordinary client messages.
[DeviceTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandAckedEvent")]
public sealed record ClientCommandAckedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, Guid CommandId) : BaseEvent, IClientContext;
