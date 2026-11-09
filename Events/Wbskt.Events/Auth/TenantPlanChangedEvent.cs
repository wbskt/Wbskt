using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Events.Auth;

/// <summary>
/// A tenant was put on another plan, or its negotiated limits changed. Written to the auth database's
/// outbox in the same transaction as the change (<c>dbo.TenantPlan_Set</c>). The management host
/// answers by copying the tenant's workspace accounts afresh from the auth host
/// (<c>TenantPlanChangedHandler</c>), rather than from this event, so events handled out of order
/// still leave the latest plan; its periodic sync repairs a copy the event never reached. Logged in
/// each of the tenant's workspaces, where <see cref="Source"/>, <see cref="EndsAt"/> and
/// <see cref="LimitOverrides"/> say who set the plan, until when, and what was negotiated.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record TenantPlanChangedEvent(int TenantId, Guid TenantRefId, PlanCode PlanCode, PlanCode PreviousPlanCode, int PlanRevision)
    : BaseEvent, IWorkspacesContext
{
    [SignalRPrivate] public IReadOnlyList<int> WorkspaceIds { get; init; } = [];

    public PlanSource Source { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    public PlanLimitOverrides? LimitOverrides { get; init; }
}
