using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[DeviceTraffic]
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnClientCommandFailedEvent", Scope = NotifyScope.Client)]
public sealed record ClientCommandFailedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, string Reason, Guid? CommandId = null) : BaseEvent, IClientContext
{
    /// <summary>The reason given when no socket host holds the device's connection.</summary>
    public const string NotConnectedReason = "The device is not connected.";

    /// <summary>The reason given when a command waiting for an offline device outlives its expiry.</summary>
    public const string ExpiredWhileQueuedReason = "The command expired before the device came back.";

    /// <summary>The reason given for a waiting command whose device was revoked.</summary>
    public const string RevokedWhileQueuedReason = "The device was revoked before it came back.";
}
