using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Error)]
public sealed record ClientPayloadFailedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType,
    string Reason
) : ClientEvent(ClientRefId, WorkspaceId);
