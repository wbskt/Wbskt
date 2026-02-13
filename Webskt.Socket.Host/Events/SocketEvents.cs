using Webskt.Common.Abstraction.Events;

namespace Webskt.Socket.Host.Events;

public record ClientConnectedEvent(Guid ClientRefId) : ClientLifecycleEvent(ClientRefId);

public record ClientDisconnectedEvent(Guid ClientRefId, string Reason) : ClientLifecycleEvent(ClientRefId);
