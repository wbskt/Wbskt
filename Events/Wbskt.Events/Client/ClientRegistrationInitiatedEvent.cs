using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientRegistrationInitiatedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    Guid PolicyRefId,
    string Name
) : BaseEvent;
