using Wbskt.Infrastructure;
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

    public async Task<Error> WithSeatsAsync(Error refusal, Guid tenantRef, CancellationToken cancellationToken) =>
        IsSeatRefusal(refusal) ? await WithSeatsAsync(refusal, await plans.FindByRefIdAsync(tenantRef, cancellationToken), cancellationToken) : refusal;

    public async Task<Error> WithSeatsAsync(Error refusal, int tenantId, CancellationToken cancellationToken) =>
        IsSeatRefusal(refusal) ? await WithSeatsAsync(refusal, await plans.FindByIdAsync(tenantId, cancellationToken), cancellationToken) : refusal;

    private static bool IsSeatRefusal(Error error) =>
        error.Code == SqlGuardErrors.PlanWorkspaceLimitReached.Code || error.Code == SqlGuardErrors.PlanMemberLimitReached.Code;

    private async Task<Error> WithSeatsAsync(Error refusal, TenantPlan? plan, CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            return refusal;
        }

        PlanLimits limits = LimitsOf(plan);
        (int workspaces, int members) = await plans.GetSeatsAsync(plan.TenantId, cancellationToken);
        return refusal.Code == SqlGuardErrors.PlanWorkspaceLimitReached.Code
            ? refusal.AtPlanLimit(plan.PlanCode.ToString(), limits.Workspaces, workspaces)
            : refusal.AtPlanLimit(plan.PlanCode.ToString(), limits.Members, members);
    }

    // A tenant deleted since the caller looked it up gets Free's limits; the write that follows fails on
    // the missing tenant anyway.
    private PlanLimits LimitsOf(TenantPlan? plan) =>
        plan is null ? catalog.LimitsFor(PlanCode.Free) : catalog.LimitsFor(plan.PlanCode, plan.LimitOverrides);
}
