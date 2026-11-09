using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services;

internal sealed class TenantLimits(ITenantPlanProvider plans, PlanCatalog catalog) : ITenantLimits
{
    public async Task<PlanLimits> ForTenantAsync(int tenantId, CancellationToken cancellationToken) =>
        LimitsOf(await plans.FindByIdAsync(tenantId, cancellationToken));

    public async Task<PlanLimits> ForTenantAsync(Guid tenantRef, CancellationToken cancellationToken) =>
        LimitsOf(await plans.FindByRefIdAsync(tenantRef, cancellationToken));

    // A tenant deleted since the caller looked it up gets Free's limits; the write that follows fails on
    // the missing tenant anyway.
    private PlanLimits LimitsOf(TenantPlan? plan) =>
        plan is null ? catalog.LimitsFor(PlanCode.Free) : catalog.LimitsFor(plan.PlanCode, plan.LimitOverrides);
}
