using Webskt.Events.Abstractions;

namespace Webskt.Events.Socket;

public record ClientConnectedEvent(Guid ClientRefId) : ClientLifecycleEvent(ClientRefId);

public record ClientDisconnectedEvent(Guid ClientRefId, string Reason) : ClientLifecycleEvent(ClientRefId);
