using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Plans, which this host owns: a tenant's, for operators and the payment provider's webhook, and
/// every workspace's, for the management host's copies in the main database. No user is behind these
/// calls, so they carry the shared internal key in place of a token.
/// </summary>
[ApiController]
[AllowAnonymous]
[InternalApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public class InternalPlansController : ApiControllerBase
{
    private readonly ITenantPlanService _tenantPlanService;

    public InternalPlansController(ITenantPlanService tenantPlanService)
    {
        _tenantPlanService = tenantPlanService;
    }

    [HttpGet(InternalRoutes.TenantPlan)]
    public async Task<ActionResult<TenantPlanResponse>> GetTenantPlan(Guid tenantRef, CancellationToken cancellationToken)
    {
        return MapResult(await _tenantPlanService.GetAsync(tenantRef, cancellationToken));
    }

    /// <summary>
    /// Puts the tenant on a plan as an operator; the plan and its overrides are replaced whole, and the
    /// payment provider's sync leaves the plan alone until its end date.
    /// </summary>
    [HttpPut(InternalRoutes.TenantPlan)]
    public async Task<ActionResult<TenantPlanResponse>> SetTenantPlan(Guid tenantRef, [FromBody] SetTenantPlanRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _tenantPlanService.SetAsync(tenantRef, request, PlanSource.Operator, cancellationToken));
    }

    /// <summary>
    /// Each workspace with its tenant's plan, a page at a time by workspace id: all of them for the
    /// management host's periodic sync, one tenant's after a plan change, or one workspace after it
    /// is created.
    /// </summary>
    [HttpGet(InternalRoutes.WorkspaceAccounts)]
    public async Task<ActionResult<Page<WorkspaceAccountResponse>>> GetWorkspaceAccounts(
        [FromQuery] PageRequest page,
        [FromQuery] int? tenantId,
        [FromQuery] int? workspaceId,
        CancellationToken cancellationToken)
    {
        return MapResult(await _tenantPlanService.GetWorkspaceAccountsAsync(page, tenantId, workspaceId, cancellationToken));
    }
}
