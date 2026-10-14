namespace Wbskt.Management.Host.Services.Clients;

/// <summary>The auth host's record of deleted workspaces, for the deleted workspace sweep.</summary>
public interface IDeletedWorkspacesClient
{
    /// <summary>
    /// The ids of the workspaces deleted at or after <paramref name="sinceUtc"/> and before
    /// <paramref name="untilUtc"/>. Throws when the auth host cannot answer: the sweep has nothing
    /// to go on, and the failed tick is what reports it.
    /// </summary>
    Task<IReadOnlyCollection<int>> GetDeletedBetweenAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken cancellationToken);
}
