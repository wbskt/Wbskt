using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientStatusChangedEvent(
    Guid ClientRefId,
    int ClientId,
    Guid PolicyRefId,
    int PolicyId,
    int WorkspaceId,
    byte Status) : BaseEvent, IClientContext, IPolicyContext;
