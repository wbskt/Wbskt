using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IWorkspaceRetirementProvider
{
    /// <summary>
    /// Disables the workspace's policies and workflows, revokes its clients and removes its trigger
    /// and schedule registrations (<c>dbo.Workspace_Retire</c>). Safe to call more than once.
    /// </summary>
    Task<WorkspaceRetirement> RetireAsync(int workspaceId, CancellationToken cancellationToken = default);
}
