using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPayloadDeliveredEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType
) : ClientEvent(ClientRefId, WorkspaceId);
