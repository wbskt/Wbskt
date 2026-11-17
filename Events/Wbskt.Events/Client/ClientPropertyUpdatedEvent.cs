using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

// Only the client's page shows a variable as it changes, so it goes to that client's watchers, under
// the client feed's cap, rather than to every tab in the workspace.
[ClientTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientPropertyUpdatedEvent", Scope = NotifyScope.Client)]
public sealed record ClientPropertyUpdatedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string PropertyName,
    string? OldValue, string NewValue) : BaseEvent, IClientContext;
