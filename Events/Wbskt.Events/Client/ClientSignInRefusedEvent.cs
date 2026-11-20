using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

/// <summary>
/// A client the workspace knows kept trying to sign in and was refused: a wrong secret, or a status that
/// may not connect. Raised at most once a minute per client by each instance that refused it, with the
/// attempts counted since the last one, so a device looping on a revoked credential is one line a minute
/// in the log rather than one per try. A sign-in naming no client is not raised: it belongs to no workspace.
/// </summary>
/// <param name="Reason">Why the latest attempt in the minute was refused.</param>
/// <param name="Attempts">How many attempts were refused since the previous event for this client.</param>
/// <param name="FirstAttemptAt">When the first of them was refused.</param>
/// <param name="LastAttemptAt">When the last of them was refused.</param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnClientSignInRefusedEvent")]
public sealed record ClientSignInRefusedEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    ClientSignInRefusal Reason,
    int Attempts,
    DateTimeOffset FirstAttemptAt,
    DateTimeOffset LastAttemptAt
) : BaseEvent, IClientContext;
