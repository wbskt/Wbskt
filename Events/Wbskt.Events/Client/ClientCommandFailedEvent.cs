using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnClientCommandFailedEvent")]
public sealed record ClientCommandFailedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, string Reason) : BaseEvent, IClientContext;
