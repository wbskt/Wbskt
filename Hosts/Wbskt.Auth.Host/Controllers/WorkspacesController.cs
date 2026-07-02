using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host.Controllers;

[Route("api/workspaces")]
[ApiController]
[Authorize]
public class WorkspacesController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IReferenceMapper _workspaceMapper;
    private readonly ILogger<WorkspacesController> _logger;

    public WorkspacesController(
        IWorkspaceService workspaceService,
        [FromKeyedServices(ReferenceType.Workspace)] IReferenceMapper workspaceMapper,
        ILogger<WorkspacesController> logger)
    {
        _workspaceService = workspaceService;
        _workspaceMapper = workspaceMapper;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a workspace reference and verifies if the user has the required permission within that workspace.
    /// </summary>
    /// <param name="request">The resolution request containing the workspace reference and required permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A response containing the internal workspace ID if authorized.</returns>
    [HttpPost("resolve")]
    public async Task<ActionResult<ResolvedWorkspaceResponse>> AuthorizeAndResolve([FromBody] ResolveWorkspaceRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AuthorizeAndResolve requested for WorkspaceRef: '{WorkspaceRef}', Permission: '{RequiredPermission}'", request.WorkspaceRef, request.RequiredPermission);
        
        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(request.WorkspaceRef, cancellationToken);
        if (workspaceId <= 0)
        {
            _logger.LogWarning("API: Resolve failed - Workspace with RefId: '{WorkspaceRef}' not found", request.WorkspaceRef);
            return NotFound(Error.NotFound("WORKSPACE_NOT_FOUND", "Workspace not found."));
        }
        
        var result = await _workspaceService.AuthorizeAsync(workspaceId, request.RequiredPermission, cancellationToken);
        if (result.IsSuccess)
        {
            _logger.LogInformation("API: Resolve succeeded for WorkspaceRef: '{WorkspaceRef}' (Internal ID: {WorkspaceId})", request.WorkspaceRef, workspaceId);
            return Ok(new ResolvedWorkspaceResponse(workspaceId));
        }
        
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
        
        var userIdResult = GetCurrentUserId();
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
        
        var userIdResult = GetCurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapResult(Result.Failure(userIdResult.Error));
        }

        var result = await _workspaceService.CreateWorkspaceAsync(userIdResult.Value, request, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Adds a new member to an existing workspace.
    /// </summary>
    /// <param name="workspaceRef">The unique reference ID of the workspace.</param>
    /// <param name="request">The member invitation details (email and role).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("{workspaceRef:guid}/members")]
    public async Task<IActionResult> AddMember(Guid workspaceRef, [FromBody] AddMemberRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AddMember requested for WorkspaceRef: '{WorkspaceRef}', Member: '{MemberEmail}' (Role: '{MemberRole}')", workspaceRef, request.Email, request.Role);
        
        var workspaceId = await _workspaceMapper.FindIdByRefIdAsync(workspaceRef, cancellationToken);
        if (workspaceId <= 0)
        {
            _logger.LogWarning("API: AddMember failed - Workspace with RefId: '{WorkspaceRef}' not found", workspaceRef);
            return NotFound(Error.NotFound("WORKSPACE_NOT_FOUND", "Workspace not found."));
        }
        
        var result = await _workspaceService.AddUserToWorkspaceAsync(workspaceId, request, cancellationToken);
        return MapResult(result);
    }

    private Result<int> GetCurrentUserId()
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            return Result<int>.Failure(Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access."));
        }
        return Result<int>.Success(userId);
    }

    private ActionResult MapResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return MapError(result.Error);
    }

    private ActionResult<T> MapResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapError(result.Error);
    }

    private ActionResult MapError(Error error)
    {
        _logger.LogWarning("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);
        return error.Type switch
        {
            ErrorType.Validation => BadRequest(error),
            ErrorType.NotFound => NotFound(error),
            ErrorType.Conflict => Conflict(error),
            ErrorType.Unauthorized => Unauthorized(error),
            _ => BadRequest(error)
        };
    }
}
