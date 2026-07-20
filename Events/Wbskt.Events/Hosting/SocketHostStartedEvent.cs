using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Hosting;

// Published once when a socket host starts. The management host reacts by resetting stale
// presence flags left behind by a hard crash (no ClientDisconnectedEvent was ever published
// for those connections). Single-node semantics: valid while one socket host owns all sockets.
[EventCriticality(EventCriticality.Info)]
public sealed record SocketHostStartedEvent : BaseEvent;
