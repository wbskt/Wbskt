using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[DeviceTraffic]
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnClientCommandFailedEvent")]
public sealed record ClientCommandFailedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, string Reason, Guid? CommandId = null) : BaseEvent, IClientContext
{
    /// <summary>The reason given when no socket host holds the device's connection.</summary>
    public const string NotConnectedReason = "The device is not connected.";
}
