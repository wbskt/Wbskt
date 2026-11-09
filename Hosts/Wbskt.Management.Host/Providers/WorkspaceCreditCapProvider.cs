using System.Data;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Providers;

internal sealed class WorkspaceCreditCapProvider : BaseSqlProvider, IWorkspaceCreditCapProvider
{
    public WorkspaceCreditCapProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<decimal> SetAsync(int workspaceId, decimal? monthlyCreditCap, DateOnly monthStart, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.WorkspaceCreditCap_Set",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.Add("@MonthlyCreditCap", SqlDbType.Decimal).Value = (object?)monthlyCreditCap ?? DBNull.Value;
                p["@MonthlyCreditCap"].Precision = 18;
                p["@MonthlyCreditCap"].Scale = 4;
                p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
            },
            r => r.GetDecimal(r.GetOrdinal("CreditsSpent")),
            new InvalidOperationException("WorkspaceCreditCap_Set did not return a row."),
            ct);
    }
}
