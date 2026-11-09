using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// A tenant's subscription and purchases. Payment itself happens on the provider's hosted pages: checkout
/// and portal answer the page to send the user to, and the plan or credits change when the provider's
/// webhook confirms the payment (<see cref="BillingWebhooksController"/>).
/// </summary>
[Route("api/v1/tenants/{tenantRef:guid}/billing")]
[ApiController]
[Authorize]
public class TenantBillingController(ITenantBillingService billing) : ApiControllerBase
{
    /// <summary>The tenant's plan, its subscription if it has one, and the plans and credit packs on offer.</summary>
    [HttpGet]
    [RequiresTenantPermission(PermissionNames.BillingRead)]
    public async Task<ActionResult<TenantBillingResponse>> Get([FromTenant] int tenantId, CancellationToken cancellationToken)
    {
        return MapResult(await billing.GetAsync(tenantId, cancellationToken));
    }

    /// <summary>
    /// Starts the provider's checkout for a paid plan or a credit pack, paid by the signed-in user.
    /// A tenant already subscribed changes its plan with <c>POST …/billing/plan</c> instead.
    /// </summary>
    [HttpPost("checkout")]
    [RequiresTenantPermission(PermissionNames.BillingManage)]
    public async Task<ActionResult<BillingCheckoutResponse>> Checkout([FromTenant] int tenantId, [FromBody] BillingCheckoutRequest request, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        return caller.IsFailure ? MapError(caller.Error) : MapResult(await billing.CheckoutAsync(tenantId, caller.Value, request, cancellationToken));
    }

    /// <summary>
    /// Moves the subscription to another plan or interval: an upgrade takes effect at once and the
    /// difference is charged now; a downgrade takes effect at the end of the period paid for and shows
    /// as the subscription's scheduled plan until then. Asking for the current plan drops a scheduled downgrade.
    /// </summary>
    [HttpPost("plan")]
    [RequiresTenantPermission(PermissionNames.BillingManage)]
    public async Task<ActionResult<TenantBillingResponse>> ChangePlan([FromTenant] int tenantId, [FromBody] ChangePlanRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await billing.ChangePlanAsync(tenantId, request, cancellationToken));
    }

    /// <summary>The provider's customer portal: cards, invoices, changing or cancelling the subscription.</summary>
    [HttpPost("portal")]
    [RequiresTenantPermission(PermissionNames.BillingManage)]
    public async Task<ActionResult<BillingPortalResponse>> Portal([FromTenant] int tenantId, CancellationToken cancellationToken)
    {
        return MapResult(await billing.PortalAsync(tenantId, cancellationToken));
    }
}
