using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public record ClientPayloadEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType,
    string Payload
) : ClientEvent(ClientRefId, WorkspaceId);
