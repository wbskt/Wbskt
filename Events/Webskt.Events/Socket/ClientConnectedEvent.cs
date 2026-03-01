using Webskt.Events.Abstractions;

namespace Webskt.Events.Socket;

public record ClientConnectedEvent(Guid ClientRefId, int WorkspaceId) : ClientLifecycleEvent(ClientRefId, WorkspaceId);