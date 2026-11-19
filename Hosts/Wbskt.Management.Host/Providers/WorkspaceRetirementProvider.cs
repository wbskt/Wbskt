using System.Data;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class WorkspaceRetirementProvider : BaseSqlProvider, IWorkspaceRetirementProvider
{
    public WorkspaceRetirementProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<WorkspaceRetirement> RetireAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteCollectionAsync(
            "dbo.Workspace_Retire",
            p => p.AddWithValue("@WorkspaceId", workspaceId),
            reader => (
                Kind: reader.GetString(reader.GetOrdinal("Kind")),
                Id: reader.GetInt64(reader.GetOrdinal("Id")),
                RefId: reader.GetNullableGuid("RefId"),
                PolicyId: reader.GetNullableInt32("PolicyId"),
                PolicyRefId: reader.GetNullableGuid("PolicyRefId")),
            cancellationToken);

        return new WorkspaceRetirement(
            rows.Where(r => r.Kind == "client")
                .Select(r => new RetiredClient((int)r.Id, r.RefId!.Value, r.PolicyId!.Value, r.PolicyRefId!.Value))
                .ToList(),
            rows.Where(r => r.Kind == "run").Select(r => r.Id).ToList(),
            rows.Where(r => r.Kind == "definition").Select(r => (int)r.Id).ToList());
    }

    public async Task<bool> HasLiveContentAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<bool>(
            "dbo.Workspace_HasLiveContent",
            p => p.AddWithValue("@WorkspaceId", workspaceId),
            cancellationToken);
    }

    public async Task<int> DeleteRetiredAccountsAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(
            "dbo.WorkspaceAccount_DeleteRetired",
            p =>
            {
                p.Add("@Now", SqlDbType.DateTime2).Value = nowUtc;
                p.AddWithValue("@BatchSize", batchSize);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<int>> GetPurgeableAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Workspace_GetPurgeable",
            p => p.AddWithValue("@BatchSize", batchSize),
            reader => reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            cancellationToken);
    }

    public async Task<int> PurgeAsync(int workspaceId, int batchSize, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(
            "dbo.Workspace_Purge",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@BatchSize", batchSize);
            },
            cancellationToken);
    }
}
