using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Providers;

internal sealed class AccessControlProvider : BaseSqlProvider, IAccessControlProvider
{
    public AccessControlProvider(IConfiguration configuration) : base(configuration, "AuthDBConnection") { }

    public async Task<int> FindRoleIdByRefIdAsync(Guid roleRef, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.Role_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", roleRef);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task<int> FindGroupIdByRefIdAsync(Guid groupRef, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.Group_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", groupRef);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task<bool> VerifyPermissionAsync(int userId, int tenantId, int? workspaceId, string permissionSlug, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScalarAsync<object>("dbo.Permission_Verify", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@WorkspaceId", workspaceId ?? (object)DBNull.Value);
            p.AddWithValue("@PermissionSlug", permissionSlug);
        }, cancellationToken);

        return result switch
        {
            null => false,
            bool b => b,
            int i => i == 1,
            _ => false
        };
    }

    public async Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(int userId, int workspaceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Permission_EffectiveSet",
            p =>
            {
                p.AddWithValue("@UserId", userId);
                p.AddWithValue("@WorkspaceId", workspaceId);
            },
            r => r.GetString(r.GetOrdinal("Slug")),
            cancellationToken
        );
    }

    public async Task<IPagedList<PermissionResponse>> GetPermissionsAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.Permission_GetAll",
            p =>
            {
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            r => new PermissionResponse(
                r.GetString(r.GetOrdinal("Slug")),
                r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description"))
            ),
            cancellationToken
        );
    }

    public async Task<IPagedList<RoleResponse>> GetRolesAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.Role_GetAll",
            p =>
            {
                p.AddWithValue("@TenantId", tenantId);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            r => new RoleResponse(
                r.GetGuid(r.GetOrdinal("RefId")),
                r.GetString(r.GetOrdinal("Name")),
                r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description"))
            ),
            cancellationToken
        );
    }

    public async Task<IPagedList<GroupResponse>> GetGroupsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.Group_GetAll",
            p =>
            {
                p.AddWithValue("@TenantId", tenantId);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            r => new GroupResponse(
                r.GetGuid(r.GetOrdinal("RefId")),
                r.GetString(r.GetOrdinal("Name")),
                r.IsDBNull(r.GetOrdinal("ParentGroupRefId")) ? null : r.GetGuid(r.GetOrdinal("ParentGroupRefId"))
            ),
            cancellationToken
        );
    }

    public async Task<Guid> InsertRoleAsync(string name, string? description, int tenantId, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Role_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", description ?? (object)DBNull.Value);
            p.AddWithValue("@TenantId", tenantId);
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (Guid)parameters["@RefId"].Value;
    }

    public async Task UpdateRoleAsync(int roleId, int tenantId, string name, string? description, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Role_Update", p =>
        {
            p.AddWithValue("@Id", roleId);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", description ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task DeleteRoleAsync(int roleId, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Role_Delete", p =>
        {
            p.AddWithValue("@Id", roleId);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task<Guid> InsertGroupAsync(string name, int? parentGroupId, int tenantId, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Group_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@ParentGroupId", parentGroupId ?? (object)DBNull.Value);
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (Guid)parameters["@RefId"].Value;
    }

    public async Task UpdateGroupAsync(int groupId, int tenantId, string name, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Group_Update", p =>
        {
            p.AddWithValue("@Id", groupId);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Name", name);
        }, cancellationToken);
    }

    public async Task DeleteGroupAsync(int groupId, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Group_Delete", p =>
        {
            p.AddWithValue("@Id", groupId);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task InsertUserGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserGroup_Insert", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@GroupId", groupId);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task RemoveUserGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserGroup_Remove", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@GroupId", groupId);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.RolePermission_Grant", p =>
        {
            p.AddWithValue("@RoleId", roleId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
            p.AddWithValue("@IsDeny", isDeny);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task RemoveRolePermissionAsync(int roleId, string permissionSlug, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.RolePermission_Remove", p =>
        {
            p.AddWithValue("@RoleId", roleId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserPermission_Grant", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
            p.AddWithValue("@IsDeny", isDeny);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@WorkspaceId", workspaceId ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task RemoveUserPermissionAsync(int userId, string permissionSlug, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserPermission_Remove", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@WorkspaceId", workspaceId ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task AssignUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserRole_Assign", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@RoleId", roleId);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@WorkspaceId", workspaceId ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task RemoveUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserRole_Remove", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@RoleId", roleId);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@WorkspaceId", workspaceId ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task AssignGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.GroupRole_Assign", p =>
        {
            p.AddWithValue("@GroupId", groupId);
            p.AddWithValue("@RoleId", roleId);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@WorkspaceId", workspaceId ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task RemoveGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.GroupRole_Remove", p =>
        {
            p.AddWithValue("@GroupId", groupId);
            p.AddWithValue("@RoleId", roleId);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@WorkspaceId", workspaceId ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RoleAssignmentResponse>> GetUserRolesAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.UserRole_GetAllForUser",
            p =>
            {
                p.AddWithValue("@UserId", userId);
                p.AddWithValue("@TenantId", tenantId);
            },
            MapRoleAssignment,
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<UserPermissionAssignmentResponse>> GetUserPermissionsAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.UserPermission_GetAllForUser",
            p =>
            {
                p.AddWithValue("@UserId", userId);
                p.AddWithValue("@TenantId", tenantId);
            },
            r => new UserPermissionAssignmentResponse(
                r.GetString(r.GetOrdinal("Slug")),
                r.GetBoolean(r.GetOrdinal("IsDeny")),
                r.IsDBNull(r.GetOrdinal("WorkspaceRefId")) ? null : r.GetGuid(r.GetOrdinal("WorkspaceRefId"))
            ),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<GroupMembershipResponse>> GetUserGroupsAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.UserGroup_GetAllForUser",
            p =>
            {
                p.AddWithValue("@UserId", userId);
                p.AddWithValue("@TenantId", tenantId);
            },
            r => new GroupMembershipResponse(
                r.GetGuid(r.GetOrdinal("GroupRefId")),
                r.GetString(r.GetOrdinal("GroupName"))
            ),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<RoleAssignmentResponse>> GetGroupRolesAsync(int groupId, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.GroupRole_GetAllForGroup",
            p =>
            {
                p.AddWithValue("@GroupId", groupId);
                p.AddWithValue("@TenantId", tenantId);
            },
            MapRoleAssignment,
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<RolePermissionAssignmentResponse>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.RolePermission_GetAllForRole",
            p => p.AddWithValue("@RoleId", roleId),
            r => new RolePermissionAssignmentResponse(
                r.GetString(r.GetOrdinal("Slug")),
                r.GetBoolean(r.GetOrdinal("IsDeny"))
            ),
            cancellationToken
        );
    }

    private static RoleAssignmentResponse MapRoleAssignment(SqlDataReader reader)
    {
        return new RoleAssignmentResponse(
            reader.GetGuid(reader.GetOrdinal("RoleRefId")),
            reader.GetString(reader.GetOrdinal("RoleName")),
            reader.IsDBNull(reader.GetOrdinal("WorkspaceRefId")) ? null : reader.GetGuid(reader.GetOrdinal("WorkspaceRefId"))
        );
    }
}
