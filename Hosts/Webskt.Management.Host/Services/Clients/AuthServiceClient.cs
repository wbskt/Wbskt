using Webskt.Common.Abstraction.Exceptions;

namespace Webskt.Management.Host.Services.Clients;

internal sealed class AuthServiceClient : IAuthServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthServiceClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<int> ResolveWorkspaceAsync(Guid workspaceRef, string requiredPermission, CancellationToken cancellationToken = default)
    {
        var request = new ResolveWorkspaceRequest(workspaceRef, requiredPermission);
        
        // Forward the user's JWT
        var userToken = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(userToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken.Replace("Bearer ", ""));
        }

        var response = await _httpClient.PostAsJsonAsync("/api/workspaces/resolve", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new SecurityException("Access denied to workspace.");
            }
            throw new Exception($"Failed to resolve workspace from Auth service. Status: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<ResolvedWorkspaceResponse>(cancellationToken: cancellationToken);
        return result?.WorkspaceId ?? 0;
    }
}
