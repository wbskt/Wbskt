using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCapabilitiesUpdatedEvent")]
public sealed record ClientCapabilitiesUpdatedEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    string AgentName,
    string AgentVersion,
    string Platform
) : BaseEvent, IClientContext;
