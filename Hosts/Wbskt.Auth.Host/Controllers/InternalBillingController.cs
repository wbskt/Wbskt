using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Payments this host owns, for the management host's reconcile of the main database's credit grants.
/// No user is behind these calls, so they carry the shared internal key in place of a token.
/// </summary>
[ApiController]
[AllowAnonymous]
[InternalApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public class InternalBillingController : ApiControllerBase
{
    private readonly ITenantBillingService _billingService;

    public InternalBillingController(ITenantBillingService billingService)
    {
        _billingService = billingService;
    }

    /// <summary>Every credit pack bought, with its refund and hold, a page at a time by id.</summary>
    [HttpGet(InternalRoutes.CreditPurchases)]
    public async Task<ActionResult<Page<CreditPurchaseResponse>>> GetCreditPurchases([FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        return MapResult(await _billingService.GetCreditPurchasesAsync(page, cancellationToken));
    }
}
