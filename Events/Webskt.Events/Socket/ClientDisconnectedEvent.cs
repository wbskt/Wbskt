using Webskt.Events.Abstractions;

namespace Webskt.Events.Socket;

public record ClientDisconnectedEvent(Guid ClientRefId, int WorkspaceId, string Reason) : ClientLifecycleEvent(ClientRefId, WorkspaceId);