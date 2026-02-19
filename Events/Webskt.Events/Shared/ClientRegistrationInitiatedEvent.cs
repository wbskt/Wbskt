using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record ClientRegistrationInitiatedEvent(
    Guid ClientRefId, 
    Guid PolicyRefId, 
    int WorkspaceId,
    string Name
) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
