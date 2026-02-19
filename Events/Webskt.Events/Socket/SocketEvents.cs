using Webskt.Events.Abstractions;

namespace Webskt.Events.Socket;

public record ClientConnectedEvent(Guid ClientRefId, int WorkspaceId) : ClientLifecycleEvent(ClientRefId, WorkspaceId);

public record ClientDisconnectedEvent(Guid ClientRefId, int WorkspaceId, string Reason) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
