using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPayloadReceivedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType,
    string Payload
) : ClientEvent(ClientRefId, WorkspaceId);
