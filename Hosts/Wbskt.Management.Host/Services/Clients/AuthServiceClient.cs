using Wbskt.Foundation.Abstraction.Exceptions;

namespace Wbskt.Management.Host.Services.Clients;

internal sealed class AuthServiceClient : IAuthServiceClient
{
    private readonly HttpClient _httpClient;

    public AuthServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<int> ResolveWorkspaceAsync(Guid workspaceRef, string requiredPermission, CancellationToken cancellationToken = default)
    {
        var request = new ResolveWorkspaceRequest(workspaceRef, requiredPermission);
        
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
