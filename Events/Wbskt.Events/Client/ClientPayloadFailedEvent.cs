using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Warning)]
public sealed record ClientPayloadFailedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string MessageType, string Reason) : BaseEvent, IClientContext;
