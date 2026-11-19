using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IWorkspaceFreezeProvider
{
    /// <summary>Revokes the workspace's live clients and reports them with its runs still in progress (<c>dbo.Workspace_Freeze</c>).</summary>
    Task<WorkspaceFreeze> FreezeAsync(int workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The frozen workspaces that still have a client able to connect or a run in progress
    /// (<c>dbo.Workspace_GetFrozenStillGoing</c>): a freeze whose event never stopped them.
    /// </summary>
    Task<IReadOnlyCollection<int>> GetFrozenStillGoingAsync(CancellationToken cancellationToken = default);
}
