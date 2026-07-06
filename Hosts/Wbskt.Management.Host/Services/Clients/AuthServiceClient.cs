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

    public async Task<Result<int>> ResolveWorkspaceAsync(Guid workspaceRef, PermissionSlug requiredPermission, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to resolve workspace reference: '{WorkspaceRef}' for permission: '{RequiredPermission}' via Auth Service", workspaceRef, requiredPermission);

        try
        {
            var request = new ResolveWorkspaceRequest(workspaceRef, requiredPermission);
            
            var response = await _httpClient.PostAsJsonAsync("/api/workspaces/resolve", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Workspace resolution forbidden: Access denied to workspace: '{WorkspaceRef}'", workspaceRef);
                    return Result<int>.Failure(Error.Unauthorized("WORKSPACE_FORBIDDEN", "Access denied to workspace."));
                }
                
                _logger.LogError("Auth service failed to resolve workspace: '{WorkspaceRef}'. Status: {StatusCode}", workspaceRef, response.StatusCode);
                return Result<int>.Failure(Error.Failure("WORKSPACE_RESOLVE_ERROR", $"Failed to resolve workspace from Auth service. Status: {response.StatusCode}"));
            }

            var result = await response.Content.ReadFromJsonAsync<ResolvedWorkspaceResponse>(cancellationToken: cancellationToken);
            var workspaceId = result?.WorkspaceId ?? 0;
            _logger.LogInformation("Successfully resolved workspace reference: '{WorkspaceRef}' to internal ID: {WorkspaceId}", workspaceRef, workspaceId);
            return Result<int>.Success(workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error resolving workspace reference: '{WorkspaceRef}'. Error: {Message}", workspaceRef, ex.Message);
            _logger.LogTrace(ex, "Workspace resolution failure stack trace for '{WorkspaceRef}'", workspaceRef);
            return Result<int>.Failure(Error.Failure("WORKSPACE_RESOLVE_EXCEPTION", ex.Message));
        }
    }
}
