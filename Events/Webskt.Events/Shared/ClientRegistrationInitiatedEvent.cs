using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record ClientRegistrationInitiatedEvent(
    Guid ClientRefId, 
    Guid PolicyRefId, 
    string Name
) : ClientLifecycleEvent(ClientRefId);
