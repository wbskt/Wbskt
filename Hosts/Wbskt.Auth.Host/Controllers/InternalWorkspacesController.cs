using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// The management host's view of a workspace. Internal: it answers with the internal workspace id,
/// which no public caller may see, so it lives under <see cref="InternalRoutes.Prefix"/>, which no
/// public router serves.
/// </summary>
[Route(InternalRoutes.Prefix + "/workspaces")]
[ApiController]
[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
public class InternalWorkspacesController : ApiControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly AuthMetrics _metrics;

    public InternalWorkspacesController(IWorkspaceService workspaceService, AuthMetrics metrics)
    {
        _workspaceService = workspaceService;
        _metrics = metrics;
    }

    /// <summary>
    /// Resolves a workspace reference and returns the caller's effective permission set within that workspace.
    /// </summary>
    /// <param name="request">The resolution request containing the workspace reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A response containing the internal workspace ID and the caller's effective permissions.</returns>
    [HttpPost("resolve")]
    public async Task<ActionResult<ResolvedWorkspaceResponse>> AuthorizeAndResolve([FromBody] ResolveWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _workspaceService.ResolveAccessAsync(userIdResult.Value, request.WorkspaceRef, cancellationToken);
        if (result.IsSuccess)
        {
            _metrics.RecordWorkspaceResolution("success");
            return Ok(new ResolvedWorkspaceResponse(result.Value.WorkspaceId, result.Value.Permissions.ToArray()));
        }

        if (result.Error.Code == "WORKSPACE_NOT_FOUND")
        {
            _metrics.RecordWorkspaceResolution("not_found");

            // Forbidden, not NotFound: "no such workspace" must read like "not yours", and the
            // management host treats anything but 401 and 403 from here as a fault. A stale reference
            // (normal after a workspace is deleted) is a permission answer, not a 500.
            return MapError(Error.Forbidden("WORKSPACE_NOT_FOUND", "Workspace not found."));
        }

        _metrics.RecordWorkspaceResolution(result.Error.Type == ErrorType.Forbidden ? "forbidden" : "error");

        return MapError(result.Error);
    }

    /// <summary>
    /// The ids of the workspaces deleted at or after <paramref name="since"/> and before
    /// <paramref name="until"/>, for the management host's deleted workspace sweep, which retires any
    /// of them that still has live clients, policies or workflows. No user is behind that call, so it
    /// carries the shared internal key in place of a token.
    /// </summary>
    [HttpGet("deleted")]
    [AllowAnonymous]
    [InternalApiKey]
    public async Task<ActionResult<DeletedWorkspacesResponse>> GetDeleted(
        [FromQuery, BindRequired] DateTimeOffset since,
        [FromQuery, BindRequired] DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var result = await _workspaceService.GetDeletedBetweenAsync(since.UtcDateTime, until.UtcDateTime, cancellationToken);
        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        return Ok(new DeletedWorkspacesResponse(result.Value.ToArray()));
    }
}
