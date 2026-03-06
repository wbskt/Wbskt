using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Error)]
public sealed record ClientPayloadFailedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType,
    string Reason
) : ClientEvent(ClientRefId, WorkspaceId);
