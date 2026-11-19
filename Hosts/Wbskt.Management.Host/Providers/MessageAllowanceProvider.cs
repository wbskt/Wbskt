using System.Data;
using System.Text.Json;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Providers;

internal sealed class MessageAllowanceProvider : BaseSqlProvider, IMessageAllowanceProvider
{
    public MessageAllowanceProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IReadOnlyCollection<WorkspaceMessageAccount>> GetAccountsAsync(DateOnly monthStart, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.WorkspaceMessageUsage_GetAccounts",
            p => p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue),
            r =>
            {
                int plan = r.GetOrdinal("PlanCode");
                int overrides = r.GetOrdinal("LimitOverrides");
                return new WorkspaceMessageAccount(
                    r.GetInt32(r.GetOrdinal("WorkspaceId")),
                    r.IsDBNull(plan) ? PlanCode.Free : Enum.Parse<PlanCode>(r.GetString(plan)),
                    r.IsDBNull(overrides) ? null : PlanLimitOverridesJson.Read(r.GetString(overrides)),
                    r.GetInt64(r.GetOrdinal("AccountMessages")),
                    r.IsDBNull(r.GetOrdinal("TenantId")) ? null : r.GetInt32(r.GetOrdinal("TenantId")),
                    r.GetInt64(r.GetOrdinal("WorkspaceMessages")));
            },
            ct);
    }

    public async Task<IReadOnlyCollection<int>> RecordNoticeAsync(IReadOnlyCollection<MessageLimitNotice> notices, DateOnly monthStart, int percent, CancellationToken ct)
    {
        if (notices.Count == 0)
        {
            return [];
        }

        return await ExecuteCollectionAsync(
            "dbo.WorkspaceMessageLimit_Record",
            p =>
            {
                p.Add("@Notices", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(notices);
                p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
                p.Add("@Percent", SqlDbType.Int).Value = percent;
            },
            r => r.GetInt32(0),
            ct);
    }

    public async Task ReleaseNoticeAsync(MessageLimitNotice notice, DateOnly monthStart, int percent, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.WorkspaceMessageLimit_Release",
            p =>
            {
                p.Add("@WorkspaceId", SqlDbType.Int).Value = notice.WorkspaceId;
                p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
                p.Add("@Percent", SqlDbType.Int).Value = percent;
                p.Add("@Allowance", SqlDbType.BigInt).Value = notice.Allowance;
            },
            ct);
    }
}
