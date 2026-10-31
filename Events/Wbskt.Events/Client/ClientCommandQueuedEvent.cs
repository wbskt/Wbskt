using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

/// <summary>
/// A command was sent to a device that was offline, and waits for it to connect. When it does, the
/// command goes out as a <see cref="ClientCommandEvent"/> with the same <see cref="CommandId"/>; if
/// <see cref="ExpiresAtUtc"/> passes first it ends as a <see cref="ClientCommandFailedEvent"/>.
/// </summary>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandQueuedEvent")]
public sealed record ClientCommandQueuedEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    string Type,
    string Payload,
    Guid CommandId,
    DateTime ExpiresAtUtc
) : ActorEvent, IClientContext;
