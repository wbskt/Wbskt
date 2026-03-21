using Wbskt.Common.Abstraction;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientPropertyUpdatedEvent")]
public sealed record ClientPropertyUpdatedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string PropertyName,
    string NewValue) : BaseEvent, IClientContext;
