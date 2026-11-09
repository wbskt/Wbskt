using System.Data;
using Wbskt.Infrastructure;

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
}
