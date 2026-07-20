using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Hosting;

// Published once when a socket host starts. The management host reacts by resetting stale
// presence flags left behind by a hard crash (no ClientDisconnectedEvent was ever published
// for those connections), scoped to this host's HostId so other live instances are untouched.
[EventCriticality(EventCriticality.Info)]
public sealed record SocketHostStartedEvent(string HostId) : BaseEvent;
