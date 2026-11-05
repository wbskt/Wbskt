using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

/// <param name="IsUpdating">
/// The client closed to install a firmware update (<see cref="Wbskt.Protocol.WebSocketBinding.UpdatingCloseCode"/>).
/// It counts as updating, not offline, until it connects again or <see cref="Wbskt.Protocol.OtaTiming.UpdateWindow"/> passes.
/// </param>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientDisconnectedEvent")]
public sealed record ClientDisconnectedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Reason, string HostId, bool IsUpdating = false) : BaseEvent, IClientContext;
