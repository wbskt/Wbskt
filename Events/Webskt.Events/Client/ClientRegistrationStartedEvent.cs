using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientRegistrationStartedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    Guid PolicyRefId
) : BaseEvent;
