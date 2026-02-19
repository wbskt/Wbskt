using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Webskt.Auth.Host.Models;
using Webskt.Auth.Host.Services;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Auth.Host.Controllers;

[Route("api/[controller]")]
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

    [HttpGet]
    public async Task<IReadOnlyCollection<WorkspaceResponse>> GetWorkspaces(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        return await _workspaceService.GetWorkspacesForUserAsync(userId, cancellationToken);
    }

    [HttpPost]
    public async Task<WorkspaceResponse> CreateWorkspace([FromBody] CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        return await _workspaceService.CreateWorkspaceAsync(userId, request, cancellationToken);
    }

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
