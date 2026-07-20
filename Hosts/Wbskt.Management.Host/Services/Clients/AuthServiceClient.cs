using Wbskt.Infrastructure;
using Wbskt.Primitives.Models;

namespace Wbskt.Management.Host.Services.Clients;

internal sealed class AuthServiceClient : IAuthServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthServiceClient> _logger;

    public AuthServiceClient(HttpClient httpClient, ILogger<AuthServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<WorkspaceAccess>> ResolveWorkspaceAsync(Guid workspaceRef, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to resolve workspace reference: '{WorkspaceRef}' via Auth Service", workspaceRef);

        try
        {
            var request = new ResolveWorkspaceRequest(workspaceRef);

            var response = await _httpClient.PostAsJsonAsync("/api/workspaces/resolve", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Workspace resolution forbidden: Access denied to workspace: '{WorkspaceRef}'", workspaceRef);
                    return Result<WorkspaceAccess>.Failure(Error.Unauthorized("WORKSPACE_FORBIDDEN", "Access denied to workspace."));
                }

                _logger.LogError("Auth service failed to resolve workspace: '{WorkspaceRef}'. Status: {StatusCode}", workspaceRef, response.StatusCode);
                return Result<WorkspaceAccess>.Failure(Error.Failure("WORKSPACE_RESOLVE_ERROR", $"Failed to resolve workspace from Auth service. Status: {response.StatusCode}"));
            }

            var result = await response.Content.ReadFromJsonAsync<ResolvedWorkspaceResponse>(cancellationToken: cancellationToken);
            if (result is null || result.WorkspaceId <= 0)
            {
                _logger.LogError("Auth service returned an empty resolve response for workspace: '{WorkspaceRef}'", workspaceRef);
                return Result<WorkspaceAccess>.Failure(Error.Failure("WORKSPACE_RESOLVE_ERROR", "Auth service returned an empty resolve response."));
            }

            var access = new WorkspaceAccess(result.WorkspaceId, result.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase));
            _logger.LogInformation("Successfully resolved workspace reference: '{WorkspaceRef}' to internal ID: {WorkspaceId} with {PermissionCount} permissions", workspaceRef, access.WorkspaceId, access.Permissions.Count);
            return Result<WorkspaceAccess>.Success(access);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error resolving workspace reference: '{WorkspaceRef}'. Error: {Message}", workspaceRef, ex.Message);
            _logger.LogTrace(ex, "Workspace resolution failure stack trace for '{WorkspaceRef}'", workspaceRef);
            return Result<WorkspaceAccess>.Failure(Error.Failure("WORKSPACE_RESOLVE_EXCEPTION", ex.Message));
        }
    }

    public Task<Result<int>> ResolveWorkspaceAsync(Guid workspaceRef, PermissionSlug requiredPermission, CancellationToken cancellationToken = default)
        => ResolveWorkspaceAsync(workspaceRef, [requiredPermission], cancellationToken);

    public async Task<Result<int>> ResolveWorkspaceAsync(Guid workspaceRef, IReadOnlyCollection<PermissionSlug> requiredPermissions, CancellationToken cancellationToken = default)
    {
        var accessResult = await ResolveWorkspaceAsync(workspaceRef, cancellationToken);
        if (accessResult.IsFailure)
        {
            return Result<int>.Failure(accessResult.Error);
        }

        var access = accessResult.Value;
        var missing = requiredPermissions.Where(p => !access.HasPermission(p)).ToList();
        if (missing.Count > 0)
        {
            _logger.LogWarning("Workspace '{WorkspaceRef}' resolved but caller lacks permission(s): {MissingPermissions}", workspaceRef, string.Join(", ", missing));
            return Result<int>.Failure(Error.Unauthorized("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {string.Join(", ", missing)}"));
        }

        return Result<int>.Success(access.WorkspaceId);
    }
}
