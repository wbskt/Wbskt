using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

/// <summary>
/// A client's connections keep replacing each other, which is what two boards signing in with the
/// same credentials look like (a cloned flash, a copied credentials file). Raised at most once per
/// <paramref name="WindowMinutes"/> per client, by the gateway that saw the replacements.
/// </summary>
/// <param name="Supersedes">How many times a newer connection replaced an older one within the window, on that gateway.</param>
[EventCriticality(EventCriticality.Warning)]
[SignalRNotify("OnClientCredentialsSharedEvent", Scope = NotifyScope.Client)]
public sealed record ClientCredentialsSharedEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    int Supersedes,
    int WindowMinutes
) : BaseEvent, IClientContext;
