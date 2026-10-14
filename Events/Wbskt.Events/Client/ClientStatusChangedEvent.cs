using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientStatusChangedEvent")]
public sealed record ClientStatusChangedEvent(
    Guid ClientRefId,
    int ClientId,
    Guid PolicyRefId,
    int PolicyId,
    int WorkspaceId,
    byte Status) : ActorEvent, IClientContext, IPolicyContext
{
    /// <summary>dbo.Clients.Status for Registered, the one status that may connect (ClientStatus in the management host).</summary>
    public const byte RegisteredStatus = 1;

    /// <summary>Whether the client may connect in its new status.</summary>
    public bool HasAccess => Status == RegisteredStatus;
}
