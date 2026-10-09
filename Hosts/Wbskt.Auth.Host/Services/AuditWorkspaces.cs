using Wbskt.Auth.Host.Providers;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// The workspaces an event about a tenant's people or a person's own account is logged in (see
/// <c>IWorkspacesContext</c>). The audit log is read per workspace, and only this host knows which
/// workspaces a tenant or a person has.
/// </summary>
internal interface IAuditWorkspaces
{
    /// <summary>Every workspace in the tenant.</summary>
    Task<IReadOnlyList<int>> OfTenantAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>Every workspace the user is a member of.</summary>
    Task<IReadOnlyList<int>> OfUserAsync(int userId, CancellationToken cancellationToken);
}

/// <remarks>
/// A failed lookup answers "none" rather than failing the action the event describes: the action has
/// already happened, and an entry missing from the audit log is the lesser harm.
/// </remarks>
internal sealed class AuditWorkspaces(IWorkspaceProvider workspaces, ILogger<AuditWorkspaces> logger) : IAuditWorkspaces
{
    /// <summary>For a service built without one, as in tests: nothing is fanned out.</summary>
    public static IAuditWorkspaces None { get; } = new NoWorkspaces();

    public Task<IReadOnlyList<int>> OfTenantAsync(int tenantId, CancellationToken cancellationToken) =>
        TryAsync(() => workspaces.GetIdsByTenantAsync(tenantId, cancellationToken), "tenant", tenantId);

    public Task<IReadOnlyList<int>> OfUserAsync(int userId, CancellationToken cancellationToken) =>
        TryAsync(() => workspaces.GetIdsByUserAsync(userId, cancellationToken), "user", userId);

    private async Task<IReadOnlyList<int>> TryAsync(Func<Task<IReadOnlyCollection<int>>> lookup, string of, int id)
    {
        try
        {
            return [.. await lookup()];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not list the workspaces of {Of} {Id} for the audit log; the event is logged in none of them.", of, id);
            return [];
        }
    }

    private sealed class NoWorkspaces : IAuditWorkspaces
    {
        public Task<IReadOnlyList<int>> OfTenantAsync(int tenantId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<int>>([]);

        public Task<IReadOnlyList<int>> OfUserAsync(int userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<int>>([]);
    }
}
