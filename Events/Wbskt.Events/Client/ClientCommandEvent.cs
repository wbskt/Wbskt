using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

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
    // The socket-host instance the sender saw holding the connection. Every instance receives
    // every command, so only this one reports a missing connection; null (the workflow engine)
    // keeps the old silent drop.
    string? TargetHostId = null,
    // Past this instant the command is refused instead of delivered, by the socket host and by the
    // SDK, so a command is never acted on late.
    DateTime? ExpiresAtUtc = null
) : BaseEvent, IClientContext;
