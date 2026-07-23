using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Controllers.Workflow;

// Public callback front door for the engine's external-facing inbound channels (WaitForHttp wakes
// and webhook triggers). Deliberately anonymous: authorization is possession of the token/path,
// which the workflow author defines at design time and hands to whoever is meant to call back -
// exactly like a webhook URL. The engine's own /api/inbound/* stays backend-network-only and
// api-key gated; these relay inward through IWorkflowEngineClient (WorkflowEngineApiKeyHandler adds
// the shared key), so the engine is never exposed publicly and manual/signal never get a public route.
//
// Hardening for the anonymous edge:
//   - per-IP rate limiting (PublicCallbackPolicy.RateLimitPolicy) throttles brute-force enumeration
//     of tokens/paths and blunts request floods;
//   - a request-body cap (RequestSizeLimit) bounds how much attacker-controlled data one call can
//     push into persisted workflow state;
//   - responses are uniform and opaque (202 Accepted, no Matched/RunId), so the response cannot be
//     used as an oracle to distinguish a live token/path from a dead one.
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(PublicCallbackPolicy.RateLimitPolicy)]
[RequestSizeLimit(PublicCallbackPolicy.MaxBodyBytes)]
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
    public async Task<IActionResult> Wake(string token, [FromBody] JsonElement payload, CancellationToken ct)
    {
        _logger.LogInformation("API: Public http-wake callback received.");

        try
        {
            WakeResponse response = await _engineClient.WakeAsync(token, payload, ct);
            // Match result is logged for operators but never returned to the anonymous caller - a
            // uniform 202 keeps the endpoint from confirming whether the token matched a parked run.
            _logger.LogInformation("Public http-wake callback outcome {Outcome} (matched={Matched}).", response.Outcome, response.Matched);
            return Accepted();
        }
        catch (HttpRequestException ex)
        {
            return EngineUnavailable(ex);
        }
    }

    [HttpPost("api/callbacks/webhook/{workspaceRef:guid}/{path}")]
    public async Task<IActionResult> Webhook(Guid workspaceRef, string path, [FromBody] JsonElement payload, CancellationToken ct)
    {
        _logger.LogInformation("API: Public webhook callback received.");

        try
        {
            WebhookResponse response = await _engineClient.WebhookAsync(workspaceRef, path, payload, ct);
            // Outcome/RunId logged for operators only; the anonymous caller gets an opaque 202 so the
            // response reveals nothing about whether the path matched a registered trigger.
            _logger.LogInformation("Public webhook callback outcome {Outcome} (runId={RunId}).", response.Outcome, response.RunId);
            return Accepted();
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
