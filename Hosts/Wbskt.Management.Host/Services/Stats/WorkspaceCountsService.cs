using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Stats;

/// <summary>
/// What a workspace holds, counted in one query. A seam of its own so a cache can sit in front of it
/// (#1098) without the controller changing.
/// </summary>
internal sealed class WorkspaceCountsService(IWorkspaceCountsProvider counts) : IWorkspaceCountsService
{
    public Task<WorkspaceCountsResponse> GetAsync(int workspaceId, CancellationToken ct) => counts.GetAsync(workspaceId, ct);
}
