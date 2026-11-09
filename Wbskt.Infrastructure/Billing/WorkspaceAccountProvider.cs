using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

public sealed class WorkspaceAccountProvider(IConfiguration configuration, PlanCatalog catalog) : BaseSqlProvider(configuration), IWorkspaceAccountProvider
{
    private readonly int _retentionGraceDays = configuration.GetValue("Billing:RetentionGraceDays", 30);

    public Task<WorkspaceAccount?> FindAsync(int workspaceId, CancellationToken ct)
    {
        return ExecuteFindAsync("dbo.WorkspaceAccount_FindBy_WorkspaceId", p => p.AddWithValue("@WorkspaceId", workspaceId), Map, ct);
    }

    public async Task<int> UpsertAsync(IReadOnlyCollection<WorkspaceAccount> accounts, CancellationToken ct)
    {
        if (accounts.Count == 0)
        {
            return 0;
        }

        // One round trip for a page of copies: the procedure reads them with OPENJSON. The monthly credits and
        // retention go with each copy, worked out from the catalogue here, so the credit poster can draw past
        // them in SQL and a copy moving to shorter retention can hold what it had.
        string json = JsonSerializer.Serialize(accounts.Select(account =>
        {
            PlanLimits limits = catalog.LimitsFor(account.PlanCode, account.LimitOverrides);
            return new
            {
                account.WorkspaceId,
                account.TenantId,
                account.TenantRefId,
                PlanCode = account.PlanCode.ToString(),
                LimitOverrides = PlanLimitOverridesJson.Write(account.LimitOverrides),
                account.PlanRevision,
                limits.CreditsPerMonth,
                limits.RawReadingsDays,
                limits.HourlyReadingsDays,
                limits.RunHistoryDays
            };
        }));

        return await ExecuteScalarAsync<int>("dbo.WorkspaceAccount_Upsert", p =>
        {
            p.AddWithValue("@Accounts", json);
            p.AddWithValue("@RetentionGraceDays", _retentionGraceDays);
        }, ct);
    }

    private static WorkspaceAccount Map(SqlDataReader r)
    {
        int overrides = r.GetOrdinal("LimitOverrides");
        return new WorkspaceAccount(
            r.GetInt32(r.GetOrdinal("WorkspaceId")),
            r.GetInt32(r.GetOrdinal("TenantId")),
            r.GetGuid(r.GetOrdinal("TenantRefId")),
            Enum.Parse<PlanCode>(r.GetString(r.GetOrdinal("PlanCode"))),
            r.IsDBNull(overrides) ? null : PlanLimitOverridesJson.Read(r.GetString(overrides)),
            r.GetInt32(r.GetOrdinal("PlanRevision")));
    }
}
