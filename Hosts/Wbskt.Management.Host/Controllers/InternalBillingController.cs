using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Controllers;

/// <summary>
/// Billing reports for operators. No user is behind these calls and the route is never public, so they
/// carry the shared internal key in place of a token.
/// </summary>
[ApiController]
[AllowAnonymous]
[InternalApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalBillingController : ApiControllerBase
{
    private readonly IBusinessOverageService _overage;

    public InternalBillingController(IBusinessOverageService overage)
    {
        _overage = overage;
    }

    /// <summary>Each Business tenant's messages and credits past its contract in a calendar month (UTC), with the amount owed.</summary>
    [HttpGet(InternalRoutes.BusinessOverage)]
    public async Task<ActionResult<BusinessOverageReport>> GetBusinessOverage([FromQuery] int year, [FromQuery] int month, CancellationToken cancellationToken)
    {
        return MapResult(await _overage.GetAsync(year, month, cancellationToken));
    }
}
