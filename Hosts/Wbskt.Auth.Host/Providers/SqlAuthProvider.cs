using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Providers;

internal sealed class SqlAuthProvider : BaseSqlProvider, IAuthProvider
{
    public SqlAuthProvider(IConfiguration configuration) : base(configuration, "AuthDBConnection") { }

    public async Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.User_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        }, cancellationToken);
    }

    public async Task<User> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.User_GetBy_Email",
            p => p.AddWithValue("@Email", email),
            MapUser,
            new SecurityException($"User with email {email} not found."),
            cancellationToken
        );
    }

    public async Task<User> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.User_GetBy_Id",
            p => p.AddWithValue("@Id", id),
            MapUser,
            new SecurityException($"User with id {id} not found."),
            cancellationToken
        );
    }

    public async Task<int> InsertUserAsync(User user, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.User_Create", p =>
        {
            p.AddWithValue("@Username", user.Username);
            p.AddWithValue("@Email", user.Email);
            p.AddWithValue("@PasswordHash", user.PasswordHash);
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (int)parameters["@Id"].Value;
    }

    public async Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.RefreshToken_Insert", p =>
        {
            p.AddWithValue("@UserId", token.UserId);
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = SecurityTokens.Hash(token.Token);
            p.AddWithValue("@Expires", token.Expires);
            p.AddWithValue("@CreatedByIp", ipAddress ?? (object)DBNull.Value);
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        token.Id = (int)parameters["@Id"].Value;
    }

    public async Task<RefreshToken> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.RefreshToken_GetBy_Token",
            p => p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = SecurityTokens.Hash(token),
            MapRefreshToken,
            new SecurityException("Invalid refresh token."),
            cancellationToken
        );
    }

    public async Task<int> RevokeRefreshTokenAsync(string token, string ipAddress, string? replacedByToken, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.RefreshToken_Revoke", p =>
        {
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = SecurityTokens.Hash(token);
            p.AddWithValue("@RevokedByIp", ipAddress ?? (object)DBNull.Value);
            p.Add("@ReplacedByTokenHash", SqlDbType.VarBinary, 32).Value =
                replacedByToken is null ? DBNull.Value : SecurityTokens.Hash(replacedByToken);
        }, cancellationToken);
    }

    public async Task<int> RevokeAllRefreshTokensForUserAsync(int userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.RefreshToken_RevokeAllForUser", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@RevokedByIp", ipAddress ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task CreatePasswordResetTokenAsync(int userId, byte[] tokenHash, DateTime expiresAt, string? requestedByIp, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.PasswordResetToken_Create", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            p.AddWithValue("@ExpiresAt", expiresAt);
            p.AddWithValue("@RequestedByIp", (object?)requestedByIp ?? DBNull.Value);
        }, cancellationToken);
    }

    public async Task<int> ConsumePasswordResetTokenAsync(byte[] tokenHash, string passwordHash, string? revokedByIp, CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = await ExecuteNonQueryAsync("dbo.PasswordResetToken_Consume", p =>
            {
                p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
                p.AddWithValue("@PasswordHash", passwordHash);
                p.AddWithValue("@RevokedByIp", (object?)revokedByIp ?? DBNull.Value);
                p.Add("@UserId", SqlDbType.Int).Direction = ParameterDirection.Output;
            }, cancellationToken);

            return (int)parameters["@UserId"].Value;
        }
        catch (SqlException ex) when (ex.Number == 50014)
        {
            throw new SecurityException("Password reset token is not valid.");
        }
    }

    public async Task CreateEmailVerificationTokenAsync(int userId, byte[] tokenHash, DateTime expiresAt, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.EmailVerificationToken_Create", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            p.AddWithValue("@ExpiresAt", expiresAt);
        }, cancellationToken);
    }

    public async Task<int> ConsumeEmailVerificationTokenAsync(byte[] tokenHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = await ExecuteNonQueryAsync("dbo.EmailVerificationToken_Consume", p =>
            {
                p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
                p.Add("@UserId", SqlDbType.Int).Direction = ParameterDirection.Output;
            }, cancellationToken);

            return (int)parameters["@UserId"].Value;
        }
        catch (SqlException ex) when (ex.Number == 50015)
        {
            throw new SecurityException("Email verification token is not valid.");
        }
    }

    public async Task SetUserActiveAsync(int userId, bool isActive, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.User_SetActive", p =>
        {
            p.AddWithValue("@Id", userId);
            p.AddWithValue("@IsActive", isActive);
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

    public async Task<IReadOnlyCollection<Tenant>> GetTenantsForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Tenant_GetForUser",
            p => p.AddWithValue("@UserId", userId),
            r => new Tenant
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                RefId = r.GetGuid(r.GetOrdinal("RefId")),
                Name = r.GetString(r.GetOrdinal("Name")),
                Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
            },
            cancellationToken
        );
    }

    public async Task<Guid> CreateTenantAsync(string name, string? description, int ownerUserId, string workspaceName, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Tenant_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            p.AddWithValue("@OwnerUserId", ownerUserId);
            p.AddWithValue("@WorkspaceName", workspaceName);
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
            p.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (Guid)parameters["@RefId"].Value;
    }

    public async Task UpdateTenantAsync(int tenantId, string name, string? description, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Tenant_Update", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", (object?)description ?? DBNull.Value);
        }, cancellationToken);
    }

    public async Task RemoveTenantMemberAsync(int tenantId, int userId, int newOwnerUserId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.TenantMember_Remove", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@NewOwnerUserId", newOwnerUserId);
        }, cancellationToken);
    }

    public async Task<CreatedInvitation> CreateInvitationAsync(int tenantId, string email, int? roleId, byte[] tokenHash, DateTime expiresAt, int invitedByUserId, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.TenantInvitation_Create", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Email", email);
            p.AddWithValue("@RoleId", (object?)roleId ?? DBNull.Value);
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            p.AddWithValue("@ExpiresAt", expiresAt);
            p.AddWithValue("@InvitedByUserId", invitedByUserId);
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
            p.Add("@TenantName", SqlDbType.NVarChar, 100).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return new CreatedInvitation((Guid)parameters["@RefId"].Value, (string)parameters["@TenantName"].Value);
    }

    public async Task<InvitationLookup> GetInvitationByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.TenantInvitation_GetBy_TokenHash",
            p => p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash,
            r => new InvitationLookup(
                r.GetGuid(r.GetOrdinal("RefId")),
                r.GetGuid(r.GetOrdinal("TenantRefId")),
                r.GetString(r.GetOrdinal("TenantName")),
                r.GetString(r.GetOrdinal("Email")),
                r.GetDateTime(r.GetOrdinal("ExpiresAt")),
                r.GetBoolean(r.GetOrdinal("IsLive"))),
            new SecurityException("Invitation not found."),
            cancellationToken
        );
    }

    public async Task<int> AcceptInvitationAsync(byte[] tokenHash, int userId, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.TenantInvitation_Accept", p =>
        {
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            p.AddWithValue("@UserId", userId);
            p.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (int)parameters["@TenantId"].Value;
    }

    public async Task RevokeInvitationAsync(Guid invitationRef, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.TenantInvitation_Revoke", p =>
        {
            p.AddWithValue("@RefId", invitationRef);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task<IPagedList<InvitationResponse>> GetInvitationsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.TenantInvitation_GetAll",
            p =>
            {
                p.AddWithValue("@TenantId", tenantId);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapInvitation,
            cancellationToken
        );
    }

    public async Task<int> FindTenantIdByRefIdForUserAsync(Guid tenantRef, int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.Tenant_FindBy_RefIdForUser", p =>
        {
            p.AddWithValue("@RefId", tenantRef);
            p.AddWithValue("@UserId", userId);
        }, cancellationToken);
    }

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

    public async Task<int> FindUserIdByRefIdInTenantAsync(Guid userRef, int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.User_FindBy_RefIdInTenant", p =>
        {
            p.AddWithValue("@RefId", userRef);
            p.AddWithValue("@TenantId", tenantId);
        }, cancellationToken);
    }

    public async Task<IPagedList<TenantMemberResponse>> GetTenantMembersAsync(int tenantId, string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.TenantMember_GetAll",
            p =>
            {
                p.AddWithValue("@TenantId", tenantId);
                p.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapTenantMember,
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
    
    public async Task RemoveUserGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserGroup_Remove", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@GroupId", groupId);
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

    private static InvitationResponse MapInvitation(SqlDataReader reader)
    {
        return new InvitationResponse(
            reader.GetGuid(reader.GetOrdinal("RefId")),
            reader.GetString(reader.GetOrdinal("Email")),
            reader.IsDBNull(reader.GetOrdinal("RoleRefId")) ? null : reader.GetGuid(reader.GetOrdinal("RoleRefId")),
            reader.IsDBNull(reader.GetOrdinal("RoleName")) ? null : reader.GetString(reader.GetOrdinal("RoleName")),
            reader.GetDateTime(reader.GetOrdinal("ExpiresAt")),
            reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        );
    }

    private static TenantMemberResponse MapTenantMember(SqlDataReader reader)
    {
        return new TenantMemberResponse(
            reader.GetGuid(reader.GetOrdinal("RefId")),
            reader.GetString(reader.GetOrdinal("Username")),
            reader.GetString(reader.GetOrdinal("Email")),
            reader.GetBoolean(reader.GetOrdinal("IsActive"))
        );
    }

    private static User MapUser(SqlDataReader reader)
    {
        return new User
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            Username = reader.GetString(reader.GetOrdinal("Username")),
            Email = reader.GetString(reader.GetOrdinal("Email")),
            PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            IsEmailVerified = reader.GetBoolean(reader.GetOrdinal("IsEmailVerified"))
        };
    }

    private static RefreshToken MapRefreshToken(SqlDataReader reader)
    {
        return new RefreshToken
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
            Expires = reader.GetDateTime(reader.GetOrdinal("Expires")),
            Revoked = reader.IsDBNull(reader.GetOrdinal("Revoked")) ? null : reader.GetDateTime(reader.GetOrdinal("Revoked"))
        };
    }
}