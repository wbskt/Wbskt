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
    /// A tenant already subscribed changes its plan in the portal instead.
    /// </summary>
    [HttpPost("checkout")]
    [RequiresTenantPermission(PermissionNames.BillingManage)]
    public async Task<ActionResult<BillingCheckoutResponse>> Checkout([FromTenant] int tenantId, [FromBody] BillingCheckoutRequest request, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        return caller.IsFailure ? MapError(caller.Error) : MapResult(await billing.CheckoutAsync(tenantId, caller.Value, request, cancellationToken));
    }

    /// <summary>The provider's customer portal: cards, invoices, changing or cancelling the subscription.</summary>
    [HttpPost("portal")]
    [RequiresTenantPermission(PermissionNames.BillingManage)]
    public async Task<ActionResult<BillingPortalResponse>> Portal([FromTenant] int tenantId, CancellationToken cancellationToken)
    {
        return MapResult(await billing.PortalAsync(tenantId, cancellationToken));
    }
}
