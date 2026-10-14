using System.Globalization;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>
/// <c>GET /api/internal/workspaces/deleted</c> on the auth host. A background job calls it, with no
/// user token to forward, so it carries the shared internal key instead (<see cref="AuthInternalApiKeyHandler"/>).
/// </summary>
internal sealed class DeletedWorkspacesClient : IDeletedWorkspacesClient
{
    private readonly HttpClient _httpClient;

    public DeletedWorkspacesClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyCollection<int>> GetDeletedBetweenAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken cancellationToken)
    {
        string url = $"/{InternalRoutes.DeletedWorkspaces}?since={Format(sinceUtc)}&until={Format(untilUtc)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<DeletedWorkspacesResponse>(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("The auth host answered the deleted workspaces request with an empty body.");
        return body.WorkspaceIds;
    }

    private static string Format(DateTime utc) =>
        Uri.EscapeDataString(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
}
