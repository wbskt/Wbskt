using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Providers;

/// <summary>What a workspace holds (dbo.Workspace_GetCounts).</summary>
public interface IWorkspaceCountsProvider
{
    Task<WorkspaceCountsResponse> GetAsync(int workspaceId, CancellationToken ct);
}
