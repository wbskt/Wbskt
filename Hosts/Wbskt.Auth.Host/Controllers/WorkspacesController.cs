using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Primitives;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Controllers;

[Route("api/workspaces")]
[ApiController]
[Authorize]
public class WorkspacesController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IReferenceMapper _workspaceMapper;

    public WorkspacesController(
        IWorkspaceService workspaceService,
        [FromKeyedServices("Workspace")] IReferenceMapper workspaceMapper)
    {
        _workspaceService = workspaceService;
        _workspaceMapper = workspaceMapper;
    }

    /// <summary>
    /// Resolves a workspace reference and verifies if the user has the required permission within that workspace.
    /// </summary>
    /// <param name="request">The resolution request containing the workspace reference and required permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A response containing the internal workspace ID if authorized.</returns>
    /// <exception cref="SecurityException">Thrown if the workspace reference is invalid.</exception>
    [HttpPost("resolve")]
    public async Task<ResolvedWorkspaceResponse> AuthorizeAndResolve([FromBody] ResolveWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(request.WorkspaceRef, cancellationToken);
        if (workspaceId <= 0)
        {
            throw new SecurityException("Invalid workspace.");
        }
        await _workspaceService.AuthorizeAsync(workspaceId, request.RequiredPermission, cancellationToken);
        return new ResolvedWorkspaceResponse(workspaceId);
    }

    /// <summary>
    /// Retrieves all workspaces that the currently authenticated user is a member of.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of workspace responses.</returns>
    [HttpGet]
    public async Task<IReadOnlyCollection<WorkspaceResponse>> GetWorkspaces(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        return await _workspaceService.GetWorkspacesForUserAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Creates a new workspace and makes the current user its primary owner.
    /// </summary>
    /// <param name="request">The workspace creation details (name, description).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created workspace details.</returns>
    [HttpPost]
    public async Task<WorkspaceResponse> CreateWorkspace([FromBody] CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        return await _workspaceService.CreateWorkspaceAsync(userId, request, cancellationToken);
    }

    /// <summary>
    /// Adds a new member to an existing workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="request">The member invitation details (email and role).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="SecurityException">Thrown if the workspace reference is invalid or access is denied.</exception>
    [HttpPost("{workspaceRef:guid}/members")]
    public async Task AddMember(Guid workspaceRef, [FromBody] AddMemberRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(workspaceRef, cancellationToken);
        if (workspaceId <= 0)
        {
            throw new SecurityException("Access denied.");
        }
        await _workspaceService.AddUserToWorkspaceAsync(workspaceId, request, cancellationToken);
    }

    private int GetCurrentUserId()
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            throw new SecurityException("User not found in token.");
        }
        return userId;
    }
}
