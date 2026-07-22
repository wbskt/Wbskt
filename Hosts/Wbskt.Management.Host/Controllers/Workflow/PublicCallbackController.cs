using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Controllers.Workflow;

// Public callback front door for the engine's external-facing inbound channels (WaitForHttp wakes
// and webhook triggers). Deliberately anonymous: authorization is possession of the token/path,
// which the workflow author defines at design time and hands to whoever is meant to call back -
// exactly like a webhook URL. The engine's own /api/inbound/* stays backend-network-only and
// api-key gated; these relay inward through IWorkflowEngineClient (WorkflowEngineApiKeyHandler adds
// the shared key), so the engine is never exposed publicly and manual/signal never get a public route.
[ApiController]
[AllowAnonymous]
public sealed class PublicCallbackController : ControllerBase
{
    private readonly IWorkflowEngineClient _engineClient;
    private readonly ILogger<PublicCallbackController> _logger;

    public PublicCallbackController(IWorkflowEngineClient engineClient, ILogger<PublicCallbackController> logger)
    {
        _engineClient = engineClient;
        _logger = logger;
    }

    [HttpPost("api/callbacks/wake/{token}")]
    public async Task<ActionResult<WakeResponse>> Wake(string token, [FromBody] JsonElement payload, CancellationToken ct)
    {
        _logger.LogInformation("API: Public http-wake callback received.");

        try
        {
            var response = await _engineClient.WakeAsync(token, payload, ct);
            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            return EngineUnavailable(ex);
        }
    }

    [HttpPost("api/callbacks/webhook/{path}")]
    public async Task<ActionResult<WebhookResponse>> Webhook(string path, [FromBody] JsonElement payload, CancellationToken ct)
    {
        _logger.LogInformation("API: Public webhook callback received.");

        try
        {
            var response = await _engineClient.WebhookAsync(path, payload, ct);
            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            return EngineUnavailable(ex);
        }
    }

    // The engine is unreachable or rejected the relay (e.g. 503 during leader failover). Surface a
    // retryable status without leaking internal detail to the anonymous caller.
    private ActionResult EngineUnavailable(HttpRequestException ex)
    {
        _logger.LogWarning("Relaying callback to the engine failed: {Message}", ex.Message);
        Response.Headers.RetryAfter = "5";
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
