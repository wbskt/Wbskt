using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientPayloadDeliveredEvent")]
public sealed record ClientPayloadDeliveredEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string MessageType, string Payload) : BaseEvent, IClientContext;
