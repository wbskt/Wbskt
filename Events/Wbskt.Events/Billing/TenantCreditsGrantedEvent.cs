using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Billing;

/// <summary>
/// A tenant was granted workflow credits on top of its plan, spendable until <see cref="ExpiresAt"/>: a
/// credit pack bought (<see cref="Source"/> the payment provider, <see cref="SourceRef"/> the payment)
/// or an operator's grant. Written to the auth database's outbox with whatever caused it. The management
/// host records it once per source and reference (<c>dbo.TenantCreditGrant_Insert</c>), so a replay adds
/// nothing. Logged in each of the tenant's workspaces.
/// </summary>
[EventCriticality(EventCriticality.Info)]
public sealed record TenantCreditsGrantedEvent(int TenantId, Guid TenantRefId, decimal Credits, DateTimeOffset ExpiresAt, string Source, string SourceRef)
    : BaseEvent, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];
}
