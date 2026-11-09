using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Providers;

/// <summary>What a workspace and its account have used of their plan (the credit ledger, clients, runs).</summary>
public interface IWorkspaceUsageProvider
{
    Task<WorkspaceUsageSummary> GetSummaryAsync(int workspaceId, int? tenantId, DateOnly monthStart, CancellationToken ct);

    /// <summary>The workspace's credits per UTC day from <paramref name="from"/> to <paramref name="to"/>, both included; days with none are left out.</summary>
    Task<IReadOnlyCollection<DailyCreditUsage>> GetDailyCreditsAsync(int workspaceId, DateOnly from, DateOnly to, CancellationToken ct);
}
