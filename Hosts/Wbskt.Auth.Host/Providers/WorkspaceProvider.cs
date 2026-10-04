using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Providers;

internal sealed class WorkspaceProvider : BaseSqlProvider, IWorkspaceProvider
{
    public WorkspaceProvider(IConfiguration configuration) : base(configuration, "AuthDBConnection") { }

    public async Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.Workspace_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        }, cancellationToken);
    }

    public async Task<Guid> CreateWorkspaceAsync(string name, string description, int ownerId, int tenantId, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Workspace_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", description);
            p.AddWithValue("@OwnerUserId", ownerId);
            p.AddWithValue("@TenantId", tenantId);
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

    public async Task AddUserToWorkspaceAsync(int workspaceId, int userId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.WorkspaceMember_Add", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@UserId", userId);
        }, cancellationToken);
    }

    public async Task RemoveUserFromWorkspaceAsync(int workspaceId, int userId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.WorkspaceMember_Remove", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@UserId", userId);
        }, cancellationToken);
    }

    public async Task<IPagedList<TenantMemberResponse>> GetWorkspaceMembersAsync(int workspaceId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.WorkspaceMember_GetAll",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            r => new TenantMemberResponse(
                r.GetGuid(r.GetOrdinal("RefId")),
                r.GetString(r.GetOrdinal("Username")),
                r.GetString(r.GetOrdinal("Email")),
                r.GetBoolean(r.GetOrdinal("IsActive")),
                r.GetBoolean(r.GetOrdinal("IsSuspended"))
            ),
            cancellationToken
        );
    }

    public async Task UpdateWorkspaceAsync(int workspaceId, string name, string? description, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Workspace_Update", p =>
        {
            p.AddWithValue("@Id", workspaceId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", description ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task SetOwnerAsync(int workspaceId, int userId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Workspace_SetOwner", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@UserId", userId);
        }, cancellationToken);
    }

    public async Task DeleteWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Workspace_Delete", p =>
        {
            p.AddWithValue("@Id", workspaceId);
        }, cancellationToken);
    }

    public async Task<bool> VerifyWorkspaceMembershipAsync(int userId, int workspaceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<bool>("dbo.WorkspaceMember_Verify", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@WorkspaceId", workspaceId);
        }, cancellationToken);
    }

    public async Task<WorkspaceAccessResolution?> ResolveAccessAsync(int userId, Guid workspaceRef, CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteCollectionAsync(
            "dbo.Workspace_ResolveAccess",
            p =>
            {
                p.AddWithValue("@UserId", userId);
                p.AddWithValue("@WorkspaceRef", workspaceRef);
            },
            r => (
                WorkspaceId: r.GetInt32(r.GetOrdinal("WorkspaceId")),
                IsMember: r.GetBoolean(r.GetOrdinal("IsMember")),
                Slug: r.IsDBNull(r.GetOrdinal("Slug")) ? null : r.GetString(r.GetOrdinal("Slug"))),
            cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var first = rows.First();
        var permissions = rows.Where(r => r.Slug is not null).Select(r => r.Slug!).ToArray();
        return new WorkspaceAccessResolution(first.WorkspaceId, first.IsMember, permissions);
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
