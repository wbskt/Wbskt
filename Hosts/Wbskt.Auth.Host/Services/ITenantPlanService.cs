using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// The plan each tenant is on. The auth host owns it; the main database holds a copy per workspace,
/// which the management host keeps from <see cref="GetWorkspaceAccountsAsync"/> and the
/// <c>TenantPlanChangedEvent</c> every change sends.
/// </summary>
public interface ITenantPlanService
{
    Task<Result<TenantPlanResponse>> GetAsync(Guid tenantRef, CancellationToken cancellationToken);

    /// <summary>
    /// Puts the tenant on a plan, recording <paramref name="source"/> as who set it. Asking for the plan,
    /// overrides, source and end it already has changes nothing and sends nothing, so a payment webhook
    /// delivered twice is harmless. An end date is kept only for an operator's plan.
    /// </summary>
    Task<Result<TenantPlanResponse>> SetAsync(Guid tenantRef, SetTenantPlanRequest request, PlanSource source, CancellationToken cancellationToken);

    Task<Result<Page<WorkspaceAccountResponse>>> GetWorkspaceAccountsAsync(PageRequest page, int? tenantId, int? workspaceId, CancellationToken cancellationToken);
}
