using Webskt.EventBus.Abstractions;

namespace Webskt.Events.Client;

public record ClientRegistrationInitiatedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    Guid PolicyRefId,
    string Name
) : BaseEvent;
