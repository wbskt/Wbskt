using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Management;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientRegistrationInitiatedEvent")]
public sealed record ClientRegistrationInitiatedEvent(
    Guid ClientRefId,
    int ClientId,
    Guid PolicyRefId,
    int PolicyId,
    int WorkspaceId,
    string Name) : BaseEvent, IClientContext, IPolicyContext;
