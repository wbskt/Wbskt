using System.Data;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Providers;

internal sealed class CredentialRetentionProvider : BaseSqlProvider, ICredentialRetentionProvider
{
    public CredentialRetentionProvider(IConfiguration configuration) : base(configuration, "AuthDBConnection") { }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.Credential_DeleteExpired", p =>
        {
            p.Add("@CutoffUtc", SqlDbType.DateTime2).Value = cutoffUtc;
            p.AddWithValue("@BatchSize", batchSize);
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ExpiredInvitation>> AnnounceExpiredInvitationsAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync("dbo.TenantInvitation_AnnounceExpired", p =>
        {
            p.Add("@NowUtc", SqlDbType.DateTime2).Value = nowUtc;
            p.AddWithValue("@BatchSize", batchSize);
        }, r => new ExpiredInvitation(
            r.GetGuid(r.GetOrdinal("RefId")),
            r.GetInt32(r.GetOrdinal("TenantId")),
            r.GetString(r.GetOrdinal("Email")),
            DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal("ExpiresAt")), DateTimeKind.Utc)), cancellationToken);
    }
}
