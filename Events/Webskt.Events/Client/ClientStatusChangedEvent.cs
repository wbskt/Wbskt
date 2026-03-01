using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientStatusChangedEvent(Guid ClientRefId, int WorkspaceId, string NewStatus) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
