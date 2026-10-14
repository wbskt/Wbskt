using Wbskt.EventBus.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Hosting;

// Published once by a socket host as it starts (StartupPresenceResetPublisher), so the management
// host clears the presence that host id held before a crash and restart (SocketHostStartedHandler).
[EventCriticality(EventCriticality.Info)]
public sealed record SocketHostStartedEvent(string HostId) : BaseEvent;
