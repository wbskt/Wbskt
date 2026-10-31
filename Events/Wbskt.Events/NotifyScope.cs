namespace Wbskt.Events;

/// <summary>Who a <see cref="SignalRNotifyAttribute"/> event is pushed to.</summary>
public enum NotifyScope
{
    /// <summary>Every console connection joined to the event's workspace. For anything a list shows.</summary>
    Workspace,

    /// <summary>
    /// Only connections that joined the event's client with <c>JoinClient</c>. For per-device traffic,
    /// which only the device page shows and which arrives at the rate devices talk. The event must
    /// implement <c>IClientContext</c>.
    /// </summary>
    Client,
}
