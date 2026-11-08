namespace Wbskt.Infrastructure.Billing;

/// <summary>The main database's copies of workspace accounts (<c>dbo.WorkspaceAccounts</c>).</summary>
public interface IWorkspaceAccountProvider
{
    Task<WorkspaceAccount?> FindAsync(int workspaceId, CancellationToken ct);

    /// <summary>
    /// Writes each copy unless the one already held is at a later plan revision. Returns how many rows
    /// were inserted or changed; a copy identical to the one held counts for nothing.
    /// </summary>
    Task<int> UpsertAsync(IReadOnlyCollection<WorkspaceAccount> accounts, CancellationToken ct);
}
