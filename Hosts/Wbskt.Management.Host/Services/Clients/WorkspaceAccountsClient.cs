using System.Globalization;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>
/// <c>GET /api/internal/workspaces/accounts</c> on the auth host. Called by a background job and by
/// event handlers, with no user token to forward, so it carries the shared internal key
/// (<see cref="AuthInternalApiKeyHandler"/>).
/// </summary>
internal sealed class WorkspaceAccountsClient(HttpClient httpClient) : IWorkspaceAccountsClient
{
    public async Task<Page<WorkspaceAccountResponse>> GetPageAsync(string? cursor, int? tenantId, int? workspaceId, CancellationToken cancellationToken)
    {
        var query = new List<string> { $"limit={Paging.MaxPageSize}" };
        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (tenantId is not null)
        {
            query.Add($"tenantId={tenantId.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (workspaceId is not null)
        {
            query.Add($"workspaceId={workspaceId.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        using var response = await httpClient.GetAsync($"/{InternalRoutes.WorkspaceAccounts}?{string.Join('&', query)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Page<WorkspaceAccountResponse>>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The auth host answered the workspace accounts request with an empty body.");
    }

    public async Task<WorkspaceAccountResponse?> FindAsync(Guid workspaceRef, CancellationToken cancellationToken)
    {
        string path = InternalRoutes.WorkspaceAccount.Replace("{workspaceRef:guid}", workspaceRef.ToString("D"));
        using var response = await httpClient.GetAsync($"/{path}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkspaceAccountResponse>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The auth host answered the workspace account request with an empty body.");
    }
}
