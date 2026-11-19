namespace Wbskt.Events;

/// <summary>Who a <see cref="SignalRNotifyAttribute"/> event is pushed to.</summary>
public enum NotifyScope
{
    /// <summary>Every console connection joined to the event's workspace. For anything a list shows.</summary>
    Workspace,

    /// <summary>
    /// Only connections that joined the event's client with <c>JoinClient</c>. For per-client traffic,
    /// which only the client page shows and which arrives at the rate clients talk. The event must
    /// implement <c>IClientContext</c>.
    /// </summary>
    Client,

    /// <summary>
    /// Every console connection joined to any of the tenant's workspaces still in use. For news about the
    /// account as a whole, such as its pooled credits, which each workspace's console has to show. The
    /// event must implement <c>ITenantContext</c>.
    /// </summary>
    Tenant,
}
