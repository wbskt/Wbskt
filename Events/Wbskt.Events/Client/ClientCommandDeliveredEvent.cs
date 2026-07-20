using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandDeliveredEvent")]
public sealed record ClientCommandDeliveredEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, string Payload, Guid? CommandId = null) : BaseEvent, IClientContext;
