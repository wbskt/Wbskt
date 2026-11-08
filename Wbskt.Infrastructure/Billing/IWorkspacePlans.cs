namespace Wbskt.Infrastructure.Billing;

/// <summary>
/// The plan a workspace is under, for the code that enforces its limits. Cached for a short while,
/// so a plan change reaches every host within <see cref="WorkspacePlans.CacheDuration"/>.
/// </summary>
public interface IWorkspacePlans
{
    /// <summary>
    /// The workspace's plan. A workspace whose account has not been copied here yet is on Free, the
    /// plan every tenant starts on, until the copy arrives.
    /// </summary>
    Task<WorkspacePlan> GetAsync(int workspaceId, CancellationToken ct);

    /// <summary>Drops this host's cached plan for the workspace, after its copy changed here.</summary>
    void Invalidate(int workspaceId);
}
