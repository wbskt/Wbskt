using System.Data;
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
                    r.GetInt64(r.GetOrdinal("AccountMessages")));
            },
            ct);
    }

    public async Task<IReadOnlyCollection<int>> RecordNoticeAsync(IReadOnlyCollection<int> workspaceIds, DateOnly monthStart, int percent, CancellationToken ct)
    {
        if (workspaceIds.Count == 0)
        {
            return [];
        }

        return await ExecuteCollectionAsync(
            "dbo.WorkspaceMessageLimit_Record",
            p =>
            {
                p.Add("@WorkspaceIds", SqlDbType.NVarChar, -1).Value = string.Join(',', workspaceIds);
                p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
                p.Add("@Percent", SqlDbType.Int).Value = percent;
            },
            r => r.GetInt32(0),
            ct);
    }
}
