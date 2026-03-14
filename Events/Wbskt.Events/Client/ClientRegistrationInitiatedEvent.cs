using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientRegistrationInitiatedEvent")]
public sealed record ClientRegistrationInitiatedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    Guid PolicyRefId,
    string Name
) : ClientEvent(ClientRefId, WorkspaceId);
