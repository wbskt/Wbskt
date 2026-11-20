using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Stats;

public interface IWorkspaceCountsService
{
    /// <summary>The workspace's clients by state, its policies and its workflows by state.</summary>
    Task<WorkspaceCountsResponse> GetAsync(int workspaceId, CancellationToken ct);
}
