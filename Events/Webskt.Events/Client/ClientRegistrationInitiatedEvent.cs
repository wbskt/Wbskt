using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientRegistrationInitiatedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    Guid PolicyRefId,
    string Name
) : BaseEvent;
