using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

/// <summary>
/// A socket host wrote a command to the device's connection. Carries no payload: the
/// <see cref="ClientCommandEvent"/> it answers already does, and <see cref="CommandId"/> joins the
/// two in the device-traffic log, so each command is stored once.
/// </summary>
[DeviceTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandDeliveredEvent")]
public sealed record ClientCommandDeliveredEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, Guid? CommandId = null) : BaseEvent, IClientContext;
