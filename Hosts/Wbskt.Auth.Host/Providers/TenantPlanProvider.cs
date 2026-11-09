using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Providers;

internal sealed class TenantPlanProvider(IConfiguration configuration) : BaseSqlProvider(configuration, "AuthDBConnection"), ITenantPlanProvider
{
    public Task<(int Workspaces, int Members)> GetSeatsAsync(int tenantId, CancellationToken cancellationToken)
    {
        return ExecuteSingleAsync(
            "dbo.TenantSeats_GetBy_TenantId",
            p => p.AddWithValue("@TenantId", tenantId),
            r => (r.GetInt32(r.GetOrdinal("Workspaces")), r.GetInt32(r.GetOrdinal("Members"))),
            new InvalidOperationException("TenantSeats_GetBy_TenantId did not return a row."),
            cancellationToken);
    }

    public Task<TenantPlan?> FindByRefIdAsync(Guid tenantRef, CancellationToken cancellationToken)
    {
        return ExecuteFindAsync("dbo.TenantPlan_GetBy_RefId", p => p.AddWithValue("@RefId", tenantRef), MapPlan, cancellationToken);
    }

    public Task<TenantPlan?> FindByIdAsync(int tenantId, CancellationToken cancellationToken)
    {
        return ExecuteFindAsync("dbo.TenantPlan_GetBy_Id", p => p.AddWithValue("@Id", tenantId), MapPlan, cancellationToken);
    }

    public Task<TenantPlan?> SetAsync(int tenantId, PlanCode planCode, PlanLimitOverrides? limitOverrides, int expectedRevision, OutboxMessage changedEvent, CancellationToken cancellationToken)
    {
        return ExecuteFindAsync("dbo.TenantPlan_Set", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@PlanCode", planCode.ToString());
            p.AddWithValue("@PlanLimitOverrides", (object?)PlanLimitOverridesJson.Write(limitOverrides) ?? DBNull.Value);
            p.AddWithValue("@ExpectedRevision", expectedRevision);
            p.AddWithValue("@OutboxMessageType", changedEvent.MessageType);
            p.AddWithValue("@OutboxPayload", changedEvent.Payload);
        }, MapPlan, cancellationToken);
    }

    public Task<IReadOnlyCollection<WorkspaceAccountResponse>> GetWorkspaceAccountsAsync(int? afterWorkspaceId, int take, int? tenantId, int? workspaceId, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync("dbo.WorkspaceAccount_GetPage", p =>
        {
            p.AddWithValue("@AfterWorkspaceId", (object?)afterWorkspaceId ?? DBNull.Value);
            p.AddWithValue("@Take", take);
            p.AddWithValue("@TenantId", (object?)tenantId ?? DBNull.Value);
            p.AddWithValue("@WorkspaceId", (object?)workspaceId ?? DBNull.Value);
        }, r => new WorkspaceAccountResponse(
            r.GetInt32(r.GetOrdinal("WorkspaceId")),
            r.GetInt32(r.GetOrdinal("TenantId")),
            r.GetGuid(r.GetOrdinal("TenantRefId")),
            ReadPlanCode(r),
            ReadOverrides(r),
            r.GetInt32(r.GetOrdinal("PlanRevision"))), cancellationToken);
    }

    private static TenantPlan MapPlan(SqlDataReader r)
    {
        int changedAt = r.GetOrdinal("PlanChangedAt");
        return new TenantPlan(
            r.GetInt32(r.GetOrdinal("Id")),
            r.GetGuid(r.GetOrdinal("RefId")),
            ReadPlanCode(r),
            ReadOverrides(r),
            r.GetInt32(r.GetOrdinal("PlanRevision")),
            r.IsDBNull(changedAt) ? null : r.GetDateTime(changedAt));
    }

    private static PlanCode ReadPlanCode(SqlDataReader r) => Enum.Parse<PlanCode>(r.GetString(r.GetOrdinal("PlanCode")));

    private static PlanLimitOverrides? ReadOverrides(SqlDataReader r)
    {
        int ordinal = r.GetOrdinal("PlanLimitOverrides");
        return r.IsDBNull(ordinal) ? null : PlanLimitOverridesJson.Read(r.GetString(ordinal));
    }
}
