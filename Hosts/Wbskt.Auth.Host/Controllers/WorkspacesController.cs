using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Models;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host.Controllers;

[Route("api/workspaces")]
[ApiController]
[Authorize]
public class WorkspacesController : ApiControllerBase
{
    private const int MaxPageSize = 200;

    private readonly IWorkspaceService _workspaceService;
    private readonly ILogger<WorkspacesController> _logger;
    private readonly AuthMetrics _metrics;

    public WorkspacesController(
        IWorkspaceService workspaceService,
        ILogger<WorkspacesController> logger,
        AuthMetrics metrics)
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

    /// <summary>
    /// Retrieves all workspaces that the currently authenticated user is a member of.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of workspace responses.</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<WorkspaceResponse>>> GetWorkspaces(CancellationToken cancellationToken)
    {
        _logger.LogDebug("API: GetWorkspaces requested");
        
        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapResult(Result.Failure(userIdResult.Error));
        }

        var result = await _workspaceService.GetWorkspacesForUserAsync(userIdResult.Value, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Creates a new workspace and makes the current user its primary owner.
    /// </summary>
    /// <param name="request">The workspace creation details (name, description).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created workspace details.</returns>
    [HttpPost]
    public async Task<ActionResult<WorkspaceResponse>> CreateWorkspace([FromBody] CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreateWorkspace requested (Name: '{WorkspaceName}')", request.Name);
        
        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapResult(Result.Failure(userIdResult.Error));
        }

        var result = await _workspaceService.CreateWorkspaceAsync(userIdResult.Value, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>Adds a new member to an existing workspace.</summary>
    [HttpPost("{workspaceRef:guid}/members")]
    [RequiresPermission(PermissionNames.UsersManage)]
    [AnnouncesWorkspaceAccessChange]
    public async Task<IActionResult> AddMember([FromWorkspace] int workspaceId, [FromBody] AddMemberRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _workspaceService.AddUserToWorkspaceAsync(workspaceId, request, cancellationToken));
    }

    /// <summary>Lists the members of a workspace.</summary>
    [HttpGet("{workspaceRef:guid}/members")]
    [RequiresPermission(PermissionNames.UsersRead)]
    public async Task<ActionResult<ListResponse<TenantMemberResponse>>> GetMembers([FromWorkspace] int workspaceId, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        // Clamped rather than rejected, so an out-of-range page size does not fail a read.
        var result = await _workspaceService.GetMembersAsync(workspaceId, Math.Max(0, skip), Math.Clamp(take, 1, MaxPageSize), cancellationToken);
        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());

        return Ok(new ListResponse<TenantMemberResponse> { Items = result.Value });
    }

    /// <summary>
    /// Removes a member from a workspace, along with any assignments scoped to it. The owner cannot
    /// be removed.
    /// </summary>
    [HttpDelete("{workspaceRef:guid}/members/{userRef:guid}")]
    [RequiresPermission(PermissionNames.UsersManage)]
    [AnnouncesWorkspaceAccessChange]
    public async Task<IActionResult> RemoveMember([FromWorkspace] int workspaceId, Guid userRef, CancellationToken cancellationToken)
    {
        return MapResult(await _workspaceService.RemoveUserFromWorkspaceAsync(workspaceId, userRef, cancellationToken));
    }

    /// <summary>Renames a workspace or changes its description.</summary>
    [HttpPut("{workspaceRef:guid}")]
    [RequiresPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> UpdateWorkspace([FromWorkspace] int workspaceId, [FromBody] CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await _workspaceService.UpdateWorkspaceAsync(workspaceId, request, cancellationToken));
    }

    /// <summary>
    /// Makes another member of the workspace's tenant its owner, adding them to the workspace if they
    /// are not in it. The previous owner stays a member and can then be removed like anyone else.
    /// </summary>
    [HttpPut("{workspaceRef:guid}/owner")]
    [RequiresPermission(PermissionNames.UsersManage)]
    [AnnouncesWorkspaceAccessChange]
    public async Task<IActionResult> TransferOwnership([FromWorkspace] int workspaceId, [FromBody] TransferOwnershipRequest request, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        return caller.IsFailure ? MapError(caller.Error) : MapResult(await _workspaceService.TransferOwnershipAsync(caller.Value, workspaceId, request.UserRef, cancellationToken));
    }

    /// <summary>
    /// Deletes a workspace and every assignment scoped to it. What the management host holds for it
    /// is retired by the event the delete queues (see <c>WorkspaceDeletedEvent</c>).
    /// </summary>
    [HttpDelete("{workspaceRef:guid}")]
    [RequiresPermission(PermissionNames.UsersManage)]
    [AnnouncesWorkspaceAccessChange]
    public async Task<IActionResult> DeleteWorkspace([FromWorkspace] int workspaceId, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        return caller.IsFailure ? MapError(caller.Error) : MapResult(await _workspaceService.DeleteWorkspaceAsync(caller.Value, workspaceId, cancellationToken));
    }
}
