using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class WorkspaceFreezeProvider : BaseSqlProvider, IWorkspaceFreezeProvider
{
    public WorkspaceFreezeProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<WorkspaceFreeze> FreezeAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteCollectionAsync(
            "dbo.Workspace_Freeze",
            p => p.AddWithValue("@WorkspaceId", workspaceId),
            reader => (
                Kind: reader.GetString(reader.GetOrdinal("Kind")),
                Id: reader.GetInt64(reader.GetOrdinal("Id")),
                RefId: reader.IsDBNull(reader.GetOrdinal("RefId")) ? (Guid?)null : reader.GetGuid(reader.GetOrdinal("RefId")),
                PolicyId: reader.IsDBNull(reader.GetOrdinal("PolicyId")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("PolicyId")),
                PolicyRefId: reader.IsDBNull(reader.GetOrdinal("PolicyRefId")) ? (Guid?)null : reader.GetGuid(reader.GetOrdinal("PolicyRefId"))),
            cancellationToken);

        return new WorkspaceFreeze(
            rows.Where(r => r.Kind == "client")
                .Select(r => new RetiredClient((int)r.Id, r.RefId!.Value, r.PolicyId!.Value, r.PolicyRefId!.Value))
                .ToList(),
            rows.Where(r => r.Kind == "run").Select(r => r.Id).ToList());
    }
}
