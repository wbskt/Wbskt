using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPayloadEvent(
    Guid ClientRefId, 
    int ClientId,
    int WorkspaceId,
    string MessageType,
    string Payload
) : BaseEvent, IClientContext;
