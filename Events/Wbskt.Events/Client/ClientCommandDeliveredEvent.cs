using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

/// <summary>
/// A socket host wrote a command to the client's connection. Carries no payload: the
/// <see cref="ClientCommandEvent"/> it answers already does, and <see cref="CommandId"/> joins the
/// two in the client-traffic log, so each command is stored once.
/// </summary>
[ClientTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandDeliveredEvent", Scope = NotifyScope.Client)]
public sealed record ClientCommandDeliveredEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, Guid? CommandId = null) : BaseEvent, IClientContext;
