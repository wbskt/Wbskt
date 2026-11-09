using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// A payment provider's webhooks. Anonymous: the provider proves itself with a signature over the raw
/// body, which is why the body is read as text rather than bound. Unversioned, like every URL pasted
/// into a third party, and kept out of the API docs.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class BillingWebhooksController(IBillingWebhookService webhooks) : ApiControllerBase
{
    /// <summary>Larger than any event a provider sends; a bigger body is refused before it is read.</summary>
    private const long MaxBodyBytes = 256 * 1024;

    [HttpPost("api/callbacks/billing/{provider}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        string body = await reader.ReadToEndAsync(cancellationToken);
        var headers = Request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());
        return MapResult(await webhooks.ReceiveAsync(provider, headers, body, cancellationToken));
    }
}
