using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// The credits granted for <see cref="Source"/> and <see cref="SourceRef"/> were held while a chargeback
/// on their payment is open (<see cref="Held"/>), or released once it was won or withdrawn. A held grant's
/// undrawn credits do not count until it is released. <see cref="Revision"/> goes up with every hold and
/// release, so one delivered late never undoes a newer one. A chargeback lost revokes the grant instead
/// (<see cref="TenantCreditGrantRevokedEvent"/>).
/// </summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record TenantCreditGrantHoldChangedEvent(int TenantId, Guid TenantRefId, string Source, string SourceRef, bool Held, int Revision)
    : BaseEvent, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];
}
