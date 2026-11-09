using System.Data;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class WorkspaceUsageProvider : BaseSqlProvider, IWorkspaceUsageProvider
{
    public WorkspaceUsageProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<WorkspaceUsageSummary> GetSummaryAsync(int workspaceId, int? tenantId, DateOnly monthStart, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.WorkspaceUsage_GetSummary",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@TenantId", (object?)tenantId ?? DBNull.Value);
                p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
            },
            r => new WorkspaceUsageSummary(
                r.GetDecimal(r.GetOrdinal("TenantCredits")),
                r.GetDecimal(r.GetOrdinal("WorkspaceCredits")),
                r.GetInt64(r.GetOrdinal("TenantMessages")),
                r.GetInt64(r.GetOrdinal("WorkspaceMessages")),
                r.GetInt32(r.GetOrdinal("TenantClients")),
                r.GetInt32(r.GetOrdinal("WorkspaceClients")),
                r.GetInt32(r.GetOrdinal("TenantActiveRuns")),
                r.GetInt32(r.GetOrdinal("WorkspaceActiveRuns")),
                r.IsDBNull(r.GetOrdinal("MonthlyCreditCap")) ? null : r.GetDecimal(r.GetOrdinal("MonthlyCreditCap")),
                r.GetDecimal(r.GetOrdinal("CreditGrantBalance")),
                r.GetDecimal(r.GetOrdinal("WorkspaceCreditsReserved")),
                r.IsDBNull(r.GetOrdinal("CreditGrantNextExpiresAt")) ? null : DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal("CreditGrantNextExpiresAt")), DateTimeKind.Utc),
                r.GetDecimal(r.GetOrdinal("CreditGrantNextExpiring"))),
            new InvalidOperationException("WorkspaceUsage_GetSummary did not return a row."),
            ct);
    }

    public async Task<IReadOnlyCollection<DailyCreditUsage>> GetDailyCreditsAsync(int workspaceId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.WorkspaceCreditUsage_GetDaily",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.Add("@From", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
                p.Add("@To", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
            },
            r => new DailyCreditUsage(DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("Day"))), r.GetDecimal(r.GetOrdinal("Credits"))),
            ct);
    }
}
