using System.Data;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class CreditUsageProvider : BaseSqlProvider, ICreditUsageProvider
{
    public CreditUsageProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IReadOnlyCollection<WorkspaceCreditPost>> PostAsync(DateOnly day, int batchSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync("dbo.RunCredits_Post", p =>
        {
            p.Add("@Day", SqlDbType.Date).Value = day.ToDateTime(TimeOnly.MinValue);
            p.AddWithValue("@BatchSize", batchSize);
        }, r => new WorkspaceCreditPost(
            r.GetInt32(r.GetOrdinal("WorkspaceId")),
            r.GetInt32(r.GetOrdinal("Runs")),
            r.GetDecimal(r.GetOrdinal("Credits"))), ct);
    }

    public async Task<IReadOnlyCollection<CreditNotice>> GetDueNoticesAsync(DateOnly monthStart, CancellationToken ct)
    {
        return await ExecuteCollectionAsync("dbo.CreditNotices_GetDue", p =>
        {
            p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
        }, r => new CreditNotice(
            Enum.Parse<CreditNoticeKind>(r.GetString(r.GetOrdinal("Kind"))),
            r.GetInt32(r.GetOrdinal("WorkspaceId")),
            r.IsDBNull(r.GetOrdinal("TenantId")) ? null : r.GetInt32(r.GetOrdinal("TenantId")),
            r.GetDecimal(r.GetOrdinal("Limit")),
            r.GetDecimal(r.GetOrdinal("Spent")),
            r.GetInt32(r.GetOrdinal("Level")),
            r.GetInt32(r.GetOrdinal("Notified"))), ct);
    }

    public async Task MarkNotifiedAsync(CreditNotice notice, DateOnly monthStart, int percent, CancellationToken ct)
    {
        bool isCap = notice.Kind == CreditNoticeKind.Cap;
        await ExecuteNonQueryAsync(isCap ? "dbo.WorkspaceCreditCap_MarkNotified" : "dbo.TenantCreditMonth_MarkNotified", p =>
        {
            if (isCap)
            {
                p.AddWithValue("@WorkspaceId", notice.WorkspaceId);
            }
            else
            {
                p.AddWithValue("@TenantId", notice.TenantId);
            }

            p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
            p.AddWithValue("@Percent", percent);
        }, ct);
    }
}
