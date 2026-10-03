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
}
