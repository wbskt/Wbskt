using Wbskt.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Providers;

internal sealed class ClientReferenceProvider : BaseSqlProvider, IClientReferenceProvider
{
    public ClientReferenceProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.Client_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        }, cancellationToken);
    }
}
