using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

internal sealed class AdminTenantService(ITenantPlanService plans, ITenantLimits limits) : IAdminTenantService
{
    public async Task<Result<AdminTenantLimitsResponse>> GetLimitsAsync(Guid tenantRef, CancellationToken cancellationToken)
    {
        var plan = await plans.GetAsync(tenantRef, cancellationToken);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        return new AdminTenantLimitsResponse(plan.Value, await limits.ForTenantAsync(tenantRef, cancellationToken));
    }
}
