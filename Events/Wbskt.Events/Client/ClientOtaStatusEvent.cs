using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

/// <summary>
/// A device reported how a firmware update is going (<c>sys.ota.status</c>). Not device traffic: it
/// stays in the client's activity for as long as other lifecycle events do.
/// </summary>
/// <param name="State">One of <c>OtaStates</c>: accepted, downloading, installed, failed or rejected.</param>
/// <param name="CommandId">The <c>sys.ota.update</c> command this is about, when the device named it.</param>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientOtaStatusEvent", Scope = NotifyScope.Client)]
public sealed record ClientOtaStatusEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    string State,
    Guid? CommandId = null,
    int? Progress = null,
    string? Version = null,
    string? Error = null
) : BaseEvent, IClientContext;
