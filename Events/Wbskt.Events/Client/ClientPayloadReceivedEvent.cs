using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPayloadReceivedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType,
    string Payload
) : ClientEvent(ClientRefId, WorkspaceId);
