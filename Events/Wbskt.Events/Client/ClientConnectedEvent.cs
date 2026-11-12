using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientConnectedEvent")]
/// <param name="IsRestored">
/// A connection the host held all along, set back online after the stale-presence sweep expired it while
/// the host could not reach the broker. It is not a new connection: it supersedes none and starts no
/// "came online" trigger.
/// </param>
public sealed record ClientConnectedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string HostId, bool IsRestored = false) : BaseEvent, IClientContext;
