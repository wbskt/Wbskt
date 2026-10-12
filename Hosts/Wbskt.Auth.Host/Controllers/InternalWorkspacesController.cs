using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly ILogger<InternalWorkspacesController> _logger;
    private readonly AuthMetrics _metrics;

    public InternalWorkspacesController(IWorkspaceService workspaceService, ILogger<InternalWorkspacesController> logger, AuthMetrics metrics)
    {
        _workspaceService = workspaceService;
        _logger = logger;
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
        _logger.LogDebug("API: AuthorizeAndResolve requested for WorkspaceRef: '{WorkspaceRef}'", request.WorkspaceRef);

        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _workspaceService.ResolveAccessAsync(userIdResult.Value, request.WorkspaceRef, cancellationToken);
        if (result.IsSuccess)
        {
            _logger.LogDebug("API: Resolve succeeded for WorkspaceRef: '{WorkspaceRef}' (Internal ID: {WorkspaceId}, Permissions: {PermissionCount})", request.WorkspaceRef, result.Value.WorkspaceId, result.Value.Permissions.Count);
            _metrics.RecordWorkspaceResolution("success");
            return Ok(new ResolvedWorkspaceResponse(result.Value.WorkspaceId, result.Value.Permissions.ToArray()));
        }

        if (result.Error.Code == "WORKSPACE_NOT_FOUND")
        {
            _logger.LogWarning("API: Resolve failed - Workspace with RefId: '{WorkspaceRef}' not found", request.WorkspaceRef);
            _metrics.RecordWorkspaceResolution("not_found");

            // Forbidden, not NotFound: "no such workspace" must read like "not yours", and the
            // management host treats anything but 401 and 403 from here as a fault. A stale reference
            // (normal after a workspace is deleted) is a permission answer, not a 500.
            return MapError(Error.Forbidden("WORKSPACE_NOT_FOUND", "Workspace not found."));
        }

        _metrics.RecordWorkspaceResolution(result.Error.Type == ErrorType.Forbidden ? "forbidden" : "error");

        return MapError(result.Error);
    }
}
