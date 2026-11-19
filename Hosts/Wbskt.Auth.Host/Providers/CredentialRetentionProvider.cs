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
            p.AddWithValue("@QueueEvents", true);
        }, r => new ExpiredInvitation(
            r.GetGuid(r.GetOrdinal("RefId")),
            r.GetInt32(r.GetOrdinal("TenantId")),
            r.GetString(r.GetOrdinal("Email")),
            r.GetUtcDateTime("ExpiresAt")), cancellationToken);
    }

    public async Task<IReadOnlyCollection<int>> GetUnconfirmedAccountsAsync(DateTime createdBeforeUtc, DateTime nowUtc, int afterId, int batchSize, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync("dbo.User_GetUnconfirmed", p =>
        {
            p.Add("@CreatedBeforeUtc", SqlDbType.DateTime2).Value = createdBeforeUtc;
            p.Add("@NowUtc", SqlDbType.DateTime2).Value = nowUtc;
            p.AddWithValue("@AfterId", afterId);
            p.AddWithValue("@BatchSize", batchSize);
        }, r => r.GetInt32(0), cancellationToken);
    }
}
