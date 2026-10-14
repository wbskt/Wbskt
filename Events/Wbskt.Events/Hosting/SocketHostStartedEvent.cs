using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Hosting;

// No longer published: stale presence is cleared by the heartbeat lease (SocketHostHeartbeat,
// StalePresenceSweeper). Kept so the rows already in the event log still name a known type.
[EventCriticality(EventCriticality.Info)]
public sealed record SocketHostStartedEvent(string HostId) : BaseEvent;
