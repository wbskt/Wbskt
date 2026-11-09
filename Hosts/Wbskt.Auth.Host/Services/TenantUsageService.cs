using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services;

internal sealed class TenantUsageService(ITenantPlanProvider plans, PlanCatalog catalog) : ITenantUsageService
{
    public async Task<Result<TenantUsageResponse>> GetAsync(int tenantId, CancellationToken cancellationToken)
    {
        TenantPlan? plan = await plans.FindByIdAsync(tenantId, cancellationToken);
        if (plan is null)
        {
            return Result<TenantUsageResponse>.Failure(Error.Forbidden("TENANT_NOT_FOUND", "Tenant not found."));
        }

        PlanLimits limits = catalog.LimitsFor(plan.PlanCode, plan.LimitOverrides);
        (int workspaces, int members) = await plans.GetSeatsAsync(tenantId, cancellationToken);
        return Result<TenantUsageResponse>.Success(new TenantUsageResponse(
            plan.PlanCode,
            limits,
            new UsageCount(workspaces, limits.Workspaces),
            new UsageCount(members, limits.Members)));
    }
}
