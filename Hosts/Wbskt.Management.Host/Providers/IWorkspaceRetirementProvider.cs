using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IWorkspaceRetirementProvider
{
    /// <summary>
    /// Disables the workspace's policies and workflows, revokes its clients and removes its trigger
    /// and schedule registrations (<c>dbo.Workspace_Retire</c>). Safe to call more than once.
    /// </summary>
    Task<WorkspaceRetirement> RetireAsync(int workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <see cref="RetireAsync"/> would still find something to shut down: an enabled policy, a
    /// client that can connect, an enabled workflow, an integration, a trigger or schedule
    /// registration, a queued trigger event or a run in progress (<c>dbo.Workspace_HasLiveContent</c>).
    /// </summary>
    Task<bool> HasLiveContentAsync(int workspaceId, CancellationToken cancellationToken = default);
}
