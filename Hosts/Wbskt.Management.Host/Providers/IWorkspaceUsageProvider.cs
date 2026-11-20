using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Providers;

/// <summary>What a workspace and its account have used of their plan (the credit ledger, clients, runs).</summary>
public interface IWorkspaceUsageProvider
{
    /// <summary>
    /// The month holding <paramref name="now"/>, with <paramref name="creditAllowance"/> (where the caller's
    /// plan refuses new runs) for a workspace whose plan copy has not arrived.
    /// </summary>
    Task<WorkspaceUsageSummary> GetSummaryAsync(int workspaceId, int? tenantId, DateTime now, decimal creditAllowance, CancellationToken ct);

    /// <summary>The workspace's credits per UTC day from <paramref name="from"/> to <paramref name="to"/>, both included; days with none are left out.</summary>
    Task<IReadOnlyCollection<DailyCreditUsage>> GetDailyCreditsAsync(int workspaceId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>The workspace's client messages per UTC day from <paramref name="from"/> to <paramref name="to"/>, both included; days with none are left out.</summary>
    Task<IReadOnlyCollection<MessageCountBucket>> GetDailyMessagesAsync(int workspaceId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>The workspace's client messages per UTC hour in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>); hours with none are left out.</summary>
    Task<IReadOnlyCollection<MessageCountBucket>> GetHourlyMessagesAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>
    /// One client's messages per UTC hour in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), or per UTC
    /// day when <paramref name="daily"/>; buckets with none are left out.
    /// </summary>
    Task<IReadOnlyCollection<MessageCountBucket>> GetClientMessagesAsync(int clientId, DateTime fromUtc, DateTime toUtc, bool daily, CancellationToken ct);
}
