using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

/// <summary>
/// A command was sent to a client that was offline, and waits for it to connect. When it does, the
/// command goes out as a <see cref="ClientCommandEvent"/> with the same <see cref="CommandId"/>; if
/// <see cref="ExpiresAtUtc"/> passes first it ends as a <see cref="ClientCommandFailedEvent"/>.
/// Client-scoped like the client's other commands: the payload is comms history, which needs logs.read
/// (JoinClient), not just workspace.join.
/// </summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandQueuedEvent", Scope = NotifyScope.Client)]
public sealed record ClientCommandQueuedEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    string Type,
    string Payload,
    Guid CommandId,
    DateTime ExpiresAtUtc
) : ActorEvent, IClientContext;
