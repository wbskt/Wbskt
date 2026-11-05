using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[ClientTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientPropertyUpdatedEvent")]
public sealed record ClientPropertyUpdatedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string PropertyName,
    string? OldValue, string NewValue) : BaseEvent, IClientContext;
