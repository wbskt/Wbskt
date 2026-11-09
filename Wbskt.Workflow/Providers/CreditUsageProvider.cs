using System.Data;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class CreditUsageProvider : BaseSqlProvider, ICreditUsageProvider
{
    public CreditUsageProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IReadOnlyCollection<WorkspaceCreditPost>> PostAsync(DateOnly day, DateTime endedSinceUtc, int batchSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync("dbo.RunCredits_Post", p =>
        {
            p.Add("@Day", SqlDbType.Date).Value = day.ToDateTime(TimeOnly.MinValue);
            p.Add("@EndedSince", SqlDbType.DateTime2).Value = endedSinceUtc;
            p.AddWithValue("@BatchSize", batchSize);
        }, r => new WorkspaceCreditPost(
            r.GetInt32(r.GetOrdinal("WorkspaceId")),
            r.GetInt32(r.GetOrdinal("Runs")),
            r.GetDecimal(r.GetOrdinal("Credits")),
            r.GetDecimal(r.GetOrdinal("SpentThisMonth")),
            r.IsDBNull(r.GetOrdinal("MonthlyCreditCap")) ? null : r.GetDecimal(r.GetOrdinal("MonthlyCreditCap")),
            r.IsDBNull(r.GetOrdinal("TenantId")) ? null : r.GetInt32(r.GetOrdinal("TenantId")),
            r.IsDBNull(r.GetOrdinal("CreditsPerMonth")) ? null : r.GetDecimal(r.GetOrdinal("CreditsPerMonth")),
            r.IsDBNull(r.GetOrdinal("TenantSpentThisMonth")) ? null : r.GetDecimal(r.GetOrdinal("TenantSpentThisMonth"))), ct);
    }
}
