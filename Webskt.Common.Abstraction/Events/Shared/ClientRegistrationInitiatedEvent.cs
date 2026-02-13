namespace Webskt.Common.Abstraction.Events.Shared;

public record ClientRegistrationInitiatedEvent(
    Guid ClientRefId, 
    Guid PolicyRefId, 
    string Name
) : ClientLifecycleEvent(ClientRefId);
