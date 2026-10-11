using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Controllers.Workflow;

// Public callback front door for the engine's external-facing inbound channels (WaitForHttp wakes
// and webhook triggers). Deliberately anonymous: authorization is possession of the token/path,
// which the workflow author defines at design time and hands to whoever is meant to call back -
// exactly like a webhook URL. A callback is checked here and sent to the engine's durable callback
// queue (IWorkflowEngineGateway), so 202 means it is safely held: one that arrives while the engine
// restarts or fails over is handled once it is back. The engine is never exposed publicly.
//
// Hardening for the anonymous edge:
//   - rate limiting (PublicCallbackRateLimits): a webhook's budget is its workspace and path, so
//     devices forwarded through a shared network server are not starved, while a per-IP ceiling
//     across every callback throttles enumeration of tokens/paths and blunts request floods;
//   - a request-body cap (RequestSizeLimit) bounds how much attacker-controlled data one call can
//     push into persisted workflow state;
//   - responses are uniform and opaque (202 Accepted, decided before the engine looks at the
//     callback), so the response cannot be used as an oracle to tell a live token/path from a dead one.
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(PublicCallbackPolicy.RateLimitPolicy)]
[RequestSizeLimit(PublicCallbackPolicy.MaxBodyBytes)]
public sealed class PublicCallbackController : ControllerBase
{
    /// <summary>The header a webhook caller presents its trigger's shared secret in.</summary>
    public const string WebhookSecretHeader = "X-Wbskt-Secret";
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxIdempotencyKeyLength = 255;

    private static bool IsValidIdempotencyKey(string key) =>
        key.Length is > 0 and <= MaxIdempotencyKeyLength && key.All(c => c is >= '\x21' and <= '\x7e');

    private readonly IWorkflowEngineGateway _engine;
    private readonly ILogger<PublicCallbackController> _logger;

    public PublicCallbackController(IWorkflowEngineGateway engine, ILogger<PublicCallbackController> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    [HttpPost("api/callbacks/wake/{token}")]
    public async Task<IActionResult> Wake(string token, [FromBody] JsonElement payload, CancellationToken ct)
    {
        _logger.LogDebug("API: Public http-wake callback received.");

        try
        {
            await _engine.SendWakeAsync(token, payload, ct);
            return Accepted();
        }
        catch (Exception ex) when (IsEngineUnavailable(ex, ct))
        {
            return EngineUnavailable(ex);
        }
    }

    [HttpPost("api/callbacks/webhook/{workspaceRef:guid}/{path}")]
    public async Task<IActionResult> Webhook(Guid workspaceRef, string path, [FromBody] JsonElement payload, CancellationToken ct)
    {
        _logger.LogDebug("API: Public webhook callback received.");

        try
        {
            // Relayed verbatim; the engine compares it against the trigger's configured secret. Checking
            // it here would mean this host loading trigger registrations it otherwise never touches.
            string? secret = Request.Headers.TryGetValue(WebhookSecretHeader, out Microsoft.Extensions.Primitives.StringValues presented)
                ? presented.ToString()
                : null;

            // A sender's name for this delivery, so its retries start one run. Checked here, while the
            // sender can still be told: a malformed header is its mistake, not a reason to retry.
            string? idempotencyKey = Request.Headers.TryGetValue(IdempotencyKeyHeader, out Microsoft.Extensions.Primitives.StringValues key)
                ? key.ToString()
                : null;
            if (idempotencyKey is not null && !IsValidIdempotencyKey(idempotencyKey))
            {
                return BadRequest(Error.Validation("IDEMPOTENCY_KEY_INVALID", $"{IdempotencyKeyHeader} must be 1-{MaxIdempotencyKeyLength} printable characters."));
            }

            await _engine.SendWebhookAsync(workspaceRef, path, payload, secret, idempotencyKey, ct);
            return Accepted();
        }
        catch (Exception ex) when (IsEngineUnavailable(ex, ct))
        {
            return EngineUnavailable(ex);
        }
    }

    // The broker refused the callback or did not take it in time (a cancellation the caller's own
    // token did not cause). Surface a retryable status without leaking internal detail to the
    // anonymous caller: nothing was queued, so the sender should try again.
    private static bool IsEngineUnavailable(Exception ex, CancellationToken ct) =>
        ex is not OperationCanceledException || !ct.IsCancellationRequested;

    private ActionResult EngineUnavailable(Exception ex)
    {
        _logger.LogWarning("Queueing a callback for the engine failed: {Message}", ex.Message);
        Response.Headers.RetryAfter = "5";
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
