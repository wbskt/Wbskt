using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Authorization;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Models;
using Wbskt.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Management.Host.Controllers;

/// <summary>
/// A workspace's integrations: the accounts with outside services (a Telegram bot, a Discord webhook,
/// a mail relay, an ntfy topic, an HTTP credential) that its workflow steps send through. Secrets go
/// in and never come out: a response carries a hint, never the value.
/// </summary>
[Route("api/v1/workspaces/{workspaceRef:guid}/integrations")]
[ApiController]
[Authorize]
public sealed class IntegrationsController(IIntegrationService integrations) : ApiControllerBase
{
    /// <summary>
    /// The workspace's integrations by name, on one page (a workspace holds at most a few dozen), so
    /// <c>nextCursor</c> is always null and <c>totalCount</c> is set.
    /// </summary>
    /// <param name="workspaceId">The workspace named by the route's workspace reference, once the caller's permission there is checked.</param>
    /// <param name="type">Optional: only this type (<c>telegram</c>, <c>discord</c>, <c>smtp</c>, <c>ntfy</c>, <c>httpAuth</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [RequiresPermission(PermissionNames.IntegrationsRead)]
    public async Task<ActionResult<Page<IntegrationResponse>>> GetAll([FromWorkspace] int workspaceId, [FromQuery] string? type, CancellationToken ct)
    {
        var result = await integrations.GetAllAsync(workspaceId, type, ct);
        return result.IsFailure
            ? MapError(result.Error)
            : Ok(new Page<IntegrationResponse> { Items = result.Value, TotalCount = result.Value.Count });
    }

    /// <summary>One integration.</summary>
    [HttpGet("{integrationRef:guid}")]
    [RequiresPermission(PermissionNames.IntegrationsRead)]
    public async Task<ActionResult<IntegrationResponse>> Get([FromWorkspace] int workspaceId, Guid integrationRef, CancellationToken ct) =>
        MapResult(await integrations.GetAsync(workspaceId, integrationRef, ct));

    /// <summary>
    /// Adds an integration: 201 with it; 400 when it is not valid; 409 <c>INTEGRATION_NAME_TAKEN</c> or
    /// <c>INTEGRATION_LIMIT_REACHED</c>; 503 when the engine, which seals the secret, is unavailable.
    /// </summary>
    [HttpPost]
    [RequiresPermission(PermissionNames.IntegrationsManage)]
    public async Task<ActionResult<IntegrationResponse>> Create([FromWorkspace] int workspaceId, IntegrationCreateRequest request, CancellationToken ct)
    {
        var result = await integrations.CreateAsync(workspaceId, request, ct);
        return result.IsFailure
            ? MapError(result.Error)
            : CreatedAtAction(nameof(Get), new { workspaceRef = RouteData.Values["workspaceRef"], integrationRef = result.Value.RefId }, result.Value);
    }

    /// <summary>
    /// Renames it, replaces its settings, or patches its secret (fields left out keep their value).
    /// A type cannot change: <c>INTEGRATION_TYPE_IMMUTABLE</c>.
    /// </summary>
    [HttpPatch("{integrationRef:guid}")]
    [RequiresPermission(PermissionNames.IntegrationsManage)]
    public async Task<ActionResult<IntegrationResponse>> Update([FromWorkspace] int workspaceId, Guid integrationRef, IntegrationUpdateRequest request, CancellationToken ct) =>
        MapResult(await integrations.UpdateAsync(workspaceId, integrationRef, request, ct));

    /// <summary>
    /// Deletes it: 204, or 409 <c>INTEGRATION_IN_USE</c> carrying <c>usedBy</c>, the published steps
    /// that send through it. Deprecated workflows do not block a delete.
    /// </summary>
    [HttpDelete("{integrationRef:guid}")]
    [RequiresPermission(PermissionNames.IntegrationsManage)]
    public async Task<IActionResult> Delete([FromWorkspace] int workspaceId, Guid integrationRef, CancellationToken ct)
    {
        var outcome = await integrations.DeleteAsync(workspaceId, integrationRef, ct);
        if (outcome.Result.IsSuccess)
        {
            return NoContent();
        }

        var error = MapError(outcome.Result.Error);
        if (outcome.UsedBy.Count > 0 && error is ApiErrorResults.ProblemResult problem)
        {
            problem.Problem.Extensions["usedBy"] = outcome.UsedBy;
        }

        return error;
    }

    /// <summary>The steps of live workflows that send through it, published or deprecated.</summary>
    [HttpGet("{integrationRef:guid}/usage")]
    [RequiresPermission(PermissionNames.IntegrationsRead)]
    public async Task<ActionResult<IntegrationUsageResponse>> GetUsage([FromWorkspace] int workspaceId, Guid integrationRef, CancellationToken ct) =>
        MapResult(await integrations.GetUsageAsync(workspaceId, integrationRef, ct));

    /// <summary>
    /// Tries an integration, saved or not, against its service. A failed test is a 200 with
    /// <c>ok</c> false; 400 only when what was given is not a valid integration. Limited per workspace,
    /// since every call reaches out to someone else's service.
    /// </summary>
    [HttpPost("test")]
    [RequiresPermission(PermissionNames.IntegrationsManage)]
    [EnableRateLimiting(RateLimitPolicies.IntegrationTest)]
    public async Task<ActionResult<IntegrationTestResponse>> Test([FromWorkspace] int workspaceId, IntegrationTestRequest request, CancellationToken ct) =>
        MapResult(await integrations.TestAsync(workspaceId, request, ct));

    /// <summary>
    /// The chats a Telegram integration's bot has heard from in the last 24 hours, so a step's chat can
    /// be picked rather than typed. A refusal from Telegram is a 200 with <c>ok</c> false
    /// (<c>INTEGRATION_WEBHOOK_ACTIVE</c> while the bot has a webhook set); 400
    /// <c>INTEGRATION_TYPE_UNSUPPORTED</c> for any other type. Limited as a test is, since it calls Telegram.
    /// </summary>
    [HttpGet("{integrationRef:guid}/chats")]
    [RequiresPermission(PermissionNames.IntegrationsRead)]
    [EnableRateLimiting(RateLimitPolicies.IntegrationTest)]
    public async Task<ActionResult<TelegramChatsResponse>> GetChats([FromWorkspace] int workspaceId, Guid integrationRef, CancellationToken ct) =>
        MapResult(await integrations.GetTelegramChatsAsync(workspaceId, integrationRef, ct));
}
