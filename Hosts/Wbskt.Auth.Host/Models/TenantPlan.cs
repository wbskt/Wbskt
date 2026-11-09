using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Models;

/// <summary>
/// A tenant's plan as <c>dbo.Tenants</c> holds it. <c>Source</c> says who set it; <c>EndsAt</c> is when an
/// operator's plan ends.
/// </summary>
public sealed record TenantPlan(
    int TenantId,
    Guid TenantRefId,
    PlanCode PlanCode,
    PlanLimitOverrides? LimitOverrides,
    int PlanRevision,
    DateTime? PlanChangedAt,
    PlanSource Source = PlanSource.Default,
    DateTime? EndsAt = null)
{
    /// <summary>True while an operator's plan holds: it has no end date, or its end is still ahead.</summary>
    public bool IsOperatorPlanAt(DateTime now) => Source == PlanSource.Operator && (EndsAt is null || EndsAt > now);
}
