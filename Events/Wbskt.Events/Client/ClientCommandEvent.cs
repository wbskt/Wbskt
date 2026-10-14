using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCommandEvent")]
public sealed record ClientCommandEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    string Type,
    string Payload,
    Guid? CommandId = null,
    // The socket-host instance the sender saw holding the connection, from presence. Every instance
    // receives every command; only this one delivers it, or reports it as not connected. Both
    // senders (management and the workflow engine) set it; null is a sender that predates it, and
    // then any host holding the connection delivers and none reports a missing one.
    string? TargetHostId = null,
    // Past this instant the command is refused instead of delivered, by the socket host and by the
    // SDK, so a command is never acted on late.
    DateTime? ExpiresAtUtc = null
) : ActorEvent, IClientContext;
