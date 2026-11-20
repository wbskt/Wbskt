using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Providers;

internal sealed class WorkspaceProvider : BaseSqlProvider, IWorkspaceProvider
{
    public WorkspaceProvider(IConfiguration configuration) : base(configuration, "AuthDBConnection") { }

    public async Task<int?> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int?>("dbo.Workspace_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        }, cancellationToken);
    }

    public async Task<int?> FindIdByRefIdInTenantAsync(Guid workspaceRef, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int?>("dbo.Workspace_FindBy_RefIdInTenant", p =>
        {
            p.AddWithValue("@RefId", workspaceRef);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task<Workspace> CreateWorkspaceAsync(string name, string description, int ownerId, int tenantId, int? workspaceLimit, OutboxMessage created, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Workspace_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", description);
            p.AddWithValue("@OwnerUserId", ownerId);
            p.AddWithValue("@TenantId", tenantId);
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
            p.AddWithValue("@WorkspaceLimit", (object?)workspaceLimit ?? DBNull.Value);
            p.Add("@WorkspaceId", SqlDbType.Int).Direction = ParameterDirection.Output;
            p.Add("@CreatedAt", SqlDbType.DateTime2).Direction = ParameterDirection.Output;
            p.AddWithValue("@OutboxMessageType", created.MessageType);
            p.Add("@OutboxPayload", SqlDbType.NVarChar, -1).Value = created.Payload;
        }, cancellationToken);

        return new Workspace
        {
            Id = (int)parameters["@WorkspaceId"].Value,
            RefId = (Guid)parameters["@RefId"].Value,
            Name = name,
            Description = description,
            OwnerUserId = ownerId,
            TenantId = tenantId,
            CreatedAt = ((DateTime)parameters["@CreatedAt"].Value).AsUtc()
        };
    }

    public async Task<IReadOnlyCollection<MemberWorkspace>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Workspace_GetBy_UserId",
            p => p.AddWithValue("@UserId", userId),
            r => new MemberWorkspace(MapWorkspace(r), r.GetBoolean(r.GetOrdinal("IsAdmin")), r.GetInt32(r.GetOrdinal("MemberCount"))),
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

    public async Task<bool> RemoveUserFromWorkspaceAsync(int workspaceId, int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<bool>("dbo.WorkspaceMember_Remove", p =>
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
                r.GetBoolean(r.GetOrdinal("IsSuspended")),
                r.GetBoolean(r.GetOrdinal("IsOwner"))
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

    public async Task<Guid> SetOwnerAsync(int workspaceId, int userId, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Workspace_SetOwner", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@UserId", userId);
            p.Add("@PreviousOwnerRefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (Guid)parameters["@PreviousOwnerRefId"].Value;
    }

    public async Task DeleteWorkspaceAsync(int workspaceId, OutboxMessage deletedEvent, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Workspace_Delete", p =>
        {
            p.AddWithValue("@Id", workspaceId);
            p.AddWithValue("@OutboxMessageType", deletedEvent.MessageType);
            p.AddWithValue("@OutboxPayload", deletedEvent.Payload);
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
                IsFrozen: r.GetBoolean(r.GetOrdinal("IsFrozen")),
                Slug: r.GetNullableString("Slug")),
            cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var first = rows.First();
        var permissions = rows.Where(r => r.Slug is not null).Select(r => r.Slug!).ToArray();
        return new WorkspaceAccessResolution(first.WorkspaceId, first.IsMember, permissions, first.IsFrozen);
    }

    public async Task<Workspace?> FindByIdAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteFindAsync("dbo.Workspace_GetBy_Id", p => p.AddWithValue("@Id", workspaceId), MapWorkspace, cancellationToken);
    }

    public async Task<IReadOnlyCollection<int>> GetIdsByTenantAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync("dbo.Workspace_GetIdsBy_TenantId", p => p.AddWithValue("@TenantId", tenantId), MapId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<int>> GetIdsByUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync("dbo.Workspace_GetIdsBy_UserId", p => p.AddWithValue("@UserId", userId), MapId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<int>> GetDeletedBetweenAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Workspace_GetDeletedBetween",
            p =>
            {
                p.Add("@Since", SqlDbType.DateTime2).Value = sinceUtc;
                p.Add("@Until", SqlDbType.DateTime2).Value = untilUtc;
            },
            r => r.GetInt32(r.GetOrdinal("WorkspaceId")),
            cancellationToken);
    }

    private static int MapId(SqlDataReader reader) => reader.GetInt32(reader.GetOrdinal("Id"));

    private static Workspace MapWorkspace(SqlDataReader reader)
    {
        return new Workspace
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Description = reader.GetNullableString("Description"),
            OwnerUserId = reader.GetInt32(reader.GetOrdinal("OwnerUserId")),
            TenantId = reader.GetInt32(reader.GetOrdinal("TenantId")),
            TenantRefId = reader.GetGuid(reader.GetOrdinal("TenantRefId")),
            CreatedAt = reader.GetUtcDateTime("CreatedAt")
        };
    }
}
