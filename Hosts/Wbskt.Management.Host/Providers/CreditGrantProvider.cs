using System.Data;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Providers;

internal sealed class CreditGrantProvider : BaseSqlProvider, ICreditGrantProvider
{
    public CreditGrantProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<bool> InsertAsync(int tenantId, decimal credits, DateTime grantedAtUtc, DateTime expiresAtUtc, string source, string sourceRef, CancellationToken ct)
    {
        return await ExecuteScalarAsync<int>("dbo.TenantCreditGrant_Insert", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.Add("@Credits", SqlDbType.Decimal).Value = credits;
            p.Add("@GrantedAt", SqlDbType.DateTime2).Value = grantedAtUtc;
            p.Add("@ExpiresAt", SqlDbType.DateTime2).Value = expiresAtUtc;
            p.AddWithValue("@Source", source);
            p.AddWithValue("@SourceRef", sourceRef);
        }, ct) == 1;
    }

    public async Task<bool> RevokeAsync(string source, string sourceRef, DateTime revokedAtUtc, CancellationToken ct)
    {
        return await ExecuteScalarAsync<int>("dbo.TenantCreditGrant_Revoke", p =>
        {
            p.AddWithValue("@Source", source);
            p.AddWithValue("@SourceRef", sourceRef);
            p.Add("@RevokedAt", SqlDbType.DateTime2).Value = revokedAtUtc;
        }, ct) == 1;
    }

    public async Task<bool> SetHoldAsync(string source, string sourceRef, bool held, int revision, DateTime atUtc, CancellationToken ct)
    {
        return await ExecuteScalarAsync<int>("dbo.TenantCreditGrant_SetHold", p =>
        {
            p.AddWithValue("@Source", source);
            p.AddWithValue("@SourceRef", sourceRef);
            p.AddWithValue("@Held", held);
            p.AddWithValue("@Revision", revision);
            p.Add("@At", SqlDbType.DateTime2).Value = atUtc;
        }, ct) == 1;
    }

    public async Task<IReadOnlyCollection<UnsettledCreditGrantTenant>> GetUnsettledAsync(DateOnly monthStart, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.TenantCreditGrants_GetUnsettled",
            p => p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue),
            r =>
            {
                int plan = r.GetOrdinal("PlanCode");
                int overrides = r.GetOrdinal("LimitOverrides");
                return new UnsettledCreditGrantTenant(
                    r.GetInt32(r.GetOrdinal("TenantId")),
                    r.IsDBNull(plan) ? PlanCode.Free : Enum.Parse<PlanCode>(r.GetString(plan)),
                    r.IsDBNull(overrides) ? null : PlanLimitOverridesJson.Read(r.GetString(overrides)));
            },
            ct);
    }

    public async Task<decimal?> SettleAsync(int tenantId, DateOnly monthStart, decimal allowance, CancellationToken ct)
    {
        object? drawn = await ExecuteScalarAsync<object?>("dbo.TenantCreditGrants_Settle", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.Add("@MonthStart", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
            p.Add("@Allowance", SqlDbType.Decimal).Value = allowance;
        }, ct);

        return drawn is null or DBNull ? null : Convert.ToDecimal(drawn);
    }
}
