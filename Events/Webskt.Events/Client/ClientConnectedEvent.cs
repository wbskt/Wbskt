using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientConnectedEvent(Guid ClientRefId, int WorkspaceId) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
