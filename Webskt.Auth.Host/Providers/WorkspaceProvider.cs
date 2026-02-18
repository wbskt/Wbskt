using System.Data;
using Microsoft.Data.SqlClient;
using Webskt.Auth.Host.Models;
using Webskt.Common.Data;

namespace Webskt.Auth.Host.Providers;

internal sealed class WorkspaceProvider : BaseSqlProvider, IWorkspaceProvider
{
    public WorkspaceProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.Workspace_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        }, cancellationToken);
    }

    public async Task<Guid> CreateWorkspaceAsync(string name, string description, int ownerId, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Workspace_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", description);
            p.AddWithValue("@OwnerUserId", ownerId);
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (Guid)parameters["@RefId"].Value;
    }

    public async Task<IReadOnlyCollection<Workspace>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Workspace_GetBy_UserId",
            p => p.AddWithValue("@UserId", userId),
            MapWorkspace,
            cancellationToken
        );
    }

    public async Task AddUserToWorkspaceAsync(int workspaceId, int userId, byte role, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.WorkspaceMember_Add", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@Role", role);
        }, cancellationToken);
    }

    public async Task<byte?> VerifyWorkspaceMembershipAsync(int userId, int workspaceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<byte?>("dbo.WorkspaceMember_Verify", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@WorkspaceId", workspaceId);
        }, cancellationToken);
    }

    private static Workspace MapWorkspace(SqlDataReader reader)
    {
        return new Workspace
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
            OwnerUserId = reader.GetInt32(reader.GetOrdinal("OwnerUserId")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
