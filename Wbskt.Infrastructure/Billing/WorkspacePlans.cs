using Microsoft.Extensions.Caching.Memory;
using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

internal sealed class WorkspacePlans(IWorkspaceAccountProvider accounts, PlanCatalog catalog, IMemoryCache cache) : IWorkspacePlans
{
    /// <summary>
    /// How long a host trusts a plan it read. Other hosts are not told when a copy changes, so this is
    /// how late an upgrade can reach the engine or the socket hosts.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public async Task<WorkspacePlan> GetAsync(int workspaceId, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(workspaceId), out WorkspacePlan? cached) && cached is not null)
        {
            return cached;
        }

        WorkspaceAccount? account = await accounts.FindAsync(workspaceId, ct);
        WorkspacePlan plan = account is null
            ? new WorkspacePlan(workspaceId, null, PlanCode.Free, catalog.LimitsFor(PlanCode.Free))
            : new WorkspacePlan(workspaceId, account.TenantId, account.PlanCode, catalog.LimitsFor(account.PlanCode, account.LimitOverrides));

        cache.Set(Key(workspaceId), plan, CacheDuration);
        return plan;
    }

    public void Invalidate(int workspaceId) => cache.Remove(Key(workspaceId));

    private static string Key(int workspaceId) => $"workspace-plan:{workspaceId}";
}
