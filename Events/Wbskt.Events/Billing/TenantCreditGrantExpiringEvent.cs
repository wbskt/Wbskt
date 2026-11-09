using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// The credits granted for <see cref="Source"/> and <see cref="SourceRef"/> (a credit pack, or an operator's
/// grant) stop counting at <see cref="ExpiresAt"/>, within 30 days, with <see cref="Remaining"/> still
/// undrawn. Sent once per grant by the management host (<c>CreditGrantExpiryWarning</c>); the auth host
/// emails the tenant's billing admins. A grant spent to nothing is not announced.
/// </summary>
[EventCriticality(EventCriticality.Info)]
public sealed record TenantCreditGrantExpiringEvent(int TenantId, string Source, string SourceRef, decimal Remaining, DateTimeOffset ExpiresAt)
    : BaseEvent, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];
}
