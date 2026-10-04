using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host.Controllers;

[Route("api/workspaces")]
[ApiController]
[Authorize]
public class WorkspacesController : ApiControllerBase
{
    private const int MaxPageSize = 200;

    private readonly IWorkspaceService _workspaceService;
    private readonly IReferenceMapper _workspaceMapper;
    private readonly ILogger<WorkspacesController> _logger;
    private readonly AuthMetrics _metrics;

    public WorkspacesController(
        IWorkspaceService workspaceService,
        [FromKeyedServices(ReferenceType.Workspace)] IReferenceMapper workspaceMapper,
        ILogger<WorkspacesController> logger,
        AuthMetrics metrics)
    {
        _workspaceService = workspaceService;
        _workspaceMapper = workspaceMapper;
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
            return MapError(UnresolvedWorkspace());
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
        _logger.LogInformation("API: GetWorkspaces requested");
        
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

    /// <summary>
    /// Adds a new member to an existing workspace. Requires the users.manage permission in that workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="request">The member invitation details (email).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{workspaceRef:guid}/members")]
    [AnnouncesWorkspaceAccessChange]
    public async Task<IActionResult> AddMember(Guid workspaceRef, [FromBody] AddMemberRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AddMember requested for WorkspaceRef: '{WorkspaceRef}', Member: '{MemberEmail}'", workspaceRef, request.Email);

        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(workspaceRef, cancellationToken);
        if (workspaceId <= 0)
        {
            _logger.LogWarning("API: AddMember failed - Workspace with RefId: '{WorkspaceRef}' not found", workspaceRef);
            return MapError(UnresolvedWorkspace());
        }

        var result = await _workspaceService.AddUserToWorkspaceAsync(userIdResult.Value, workspaceId, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Lists the members of a workspace. Requires the users.read permission in that workspace.
    /// </summary>
    [HttpGet("{workspaceRef:guid}/members")]
    public async Task<ActionResult<ListResponse<TenantMemberResponse>>> GetMembers(Guid workspaceRef, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetMembers requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var resolved = await ResolveAsync(workspaceRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return MapError(resolved.Error);
        }

        // Clamped rather than rejected, so an out-of-range page size does not fail a read.
        var result = await _workspaceService.GetMembersAsync(
            resolved.Value.CallerId, resolved.Value.WorkspaceId, Math.Max(0, skip), Math.Clamp(take, 1, MaxPageSize), cancellationToken);

        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());

        return Ok(new ListResponse<TenantMemberResponse> { Items = result.Value });
    }

    /// <summary>
    /// Removes a member from a workspace, along with any assignments scoped to it. The owner cannot
    /// be removed. Requires the users.manage permission in that workspace.
    /// </summary>
    [HttpDelete("{workspaceRef:guid}/members/{userRef:guid}")]
    [AnnouncesWorkspaceAccessChange]
    public async Task<IActionResult> RemoveMember(Guid workspaceRef, Guid userRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RemoveMember requested for WorkspaceRef: '{WorkspaceRef}', UserRef: '{UserRef}'", workspaceRef, userRef);

        var resolved = await ResolveAsync(workspaceRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return MapError(resolved.Error);
        }

        return MapResult(await _workspaceService.RemoveUserFromWorkspaceAsync(resolved.Value.CallerId, resolved.Value.WorkspaceId, userRef, cancellationToken));
    }

    /// <summary>
    /// Renames a workspace or changes its description. Requires users.manage in that workspace.
    /// </summary>
    [HttpPut("{workspaceRef:guid}")]
    public async Task<IActionResult> UpdateWorkspace(Guid workspaceRef, [FromBody] CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: UpdateWorkspace requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var resolved = await ResolveAsync(workspaceRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return MapError(resolved.Error);
        }

        return MapResult(await _workspaceService.UpdateWorkspaceAsync(resolved.Value.CallerId, resolved.Value.WorkspaceId, request, cancellationToken));
    }

    /// <summary>
    /// Makes another member of the workspace's tenant its owner, adding them to the workspace if they
    /// are not in it. The previous owner stays a member and can then be removed like anyone else.
    /// Requires users.manage in that workspace.
    /// </summary>
    [HttpPut("{workspaceRef:guid}/owner")]
    [AnnouncesWorkspaceAccessChange]
    public async Task<IActionResult> TransferOwnership(Guid workspaceRef, [FromBody] TransferOwnershipRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: TransferOwnership requested for WorkspaceRef: '{WorkspaceRef}', NewOwner: '{UserRef}'", workspaceRef, request.UserRef);

        var resolved = await ResolveAsync(workspaceRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return MapError(resolved.Error);
        }

        return MapResult(await _workspaceService.TransferOwnershipAsync(resolved.Value.CallerId, resolved.Value.WorkspaceId, request.UserRef, cancellationToken));
    }

    /// <summary>
    /// Deletes a workspace and every assignment scoped to it. Resources owned by other services
    /// (clients, policies, workflows) are not removed — their workspace reference simply stops
    /// resolving. Requires users.manage in that workspace.
    /// </summary>
    [HttpDelete("{workspaceRef:guid}")]
    [AnnouncesWorkspaceAccessChange]
    public async Task<IActionResult> DeleteWorkspace(Guid workspaceRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: DeleteWorkspace requested for WorkspaceRef: '{WorkspaceRef}'", workspaceRef);

        var resolved = await ResolveAsync(workspaceRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return MapError(resolved.Error);
        }

        return MapResult(await _workspaceService.DeleteWorkspaceAsync(resolved.Value.CallerId, resolved.Value.WorkspaceId, cancellationToken));
    }

    /// <summary>Resolves the caller and the workspace reference together, as every scoped operation needs both.</summary>
    private async Task<Result<(int CallerId, int WorkspaceId)>> ResolveAsync(Guid workspaceRef, CancellationToken cancellationToken)
    {
        var userIdResult = CurrentUserId();
        if (userIdResult.IsFailure)
        {
            return Result<(int, int)>.Failure(userIdResult.Error);
        }

        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(workspaceRef, cancellationToken);
        if (workspaceId <= 0)
        {
            _logger.LogWarning("Workspace with RefId: '{WorkspaceRef}' not found", workspaceRef);
            return Result<(int, int)>.Failure(UnresolvedWorkspace());
        }

        return Result<(int, int)>.Success((userIdResult.Value, workspaceId));
    }

    /// <summary>
    /// A workspace reference that does not resolve. Forbidden rather than NotFound, for two reasons:
    /// it is the convention for an unresolvable RefId (see "The ID Boundary" in
    /// Docs/Coding.Conventions.md), and it keeps "no such workspace" indistinguishable from "not
    /// yours", so the endpoint cannot be used to enumerate workspaces.
    /// <para>
    /// It also matters downstream. The management host resolves every workspace-scoped request
    /// through here and only understands 401 and 403; any other status becomes an
    /// <see cref="ErrorType.Failure"/> and surfaces to the user as a 500. A stale reference — the
    /// normal state of affairs after a workspace is deleted, since resources in other services
    /// outlive it — is a permission answer, not a server fault.
    /// </para>
    /// </summary>
    private static Error UnresolvedWorkspace() => Error.Forbidden("WORKSPACE_NOT_FOUND", "Workspace not found.");
}
