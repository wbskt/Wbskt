using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPayloadDeliveredEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType
) : ClientEvent(ClientRefId, WorkspaceId);
