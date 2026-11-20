using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class WorkspaceCountsProvider : BaseSqlProvider, IWorkspaceCountsProvider
{
    public WorkspaceCountsProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<WorkspaceCountsResponse> GetAsync(int workspaceId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Workspace_GetCounts",
            p => p.AddWithValue("@WorkspaceId", workspaceId),
            r =>
            {
                int online = r.GetInt32(r.GetOrdinal("ClientsOnline"));
                int offline = r.GetInt32(r.GetOrdinal("ClientsOffline"));
                int pending = r.GetInt32(r.GetOrdinal("ClientsPending"));
                int revoked = r.GetInt32(r.GetOrdinal("ClientsRevoked"));
                int rejected = r.GetInt32(r.GetOrdinal("ClientsRejected"));
                int enabled = r.GetInt32(r.GetOrdinal("WorkflowsEnabled"));
                int disabled = r.GetInt32(r.GetOrdinal("WorkflowsDisabled"));
                int deprecated = r.GetInt32(r.GetOrdinal("WorkflowsDeprecated"));
                return new WorkspaceCountsResponse(
                    new ClientStateCounts(online + offline + pending + revoked + rejected, online, offline, pending, revoked, rejected),
                    r.GetInt32(r.GetOrdinal("Policies")),
                    new WorkflowStateCounts(enabled + disabled + deprecated, enabled, disabled, deprecated));
            },
            new InvalidOperationException("Workspace_GetCounts returned no row."),
            ct);
    }
}
