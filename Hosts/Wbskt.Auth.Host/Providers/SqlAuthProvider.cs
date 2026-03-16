using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Common.Data;
using Wbskt.Foundation.Abstraction.Exceptions;

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
            p.AddWithValue("@Token", token.Token);
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
            p => p.AddWithValue("@Token", token),
            MapRefreshToken,
            new SecurityException("Invalid refresh token."),
            cancellationToken
        );
    }

    public async Task<bool> VerifyPermissionAsync(int userId, string permissionSlug, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScalarAsync<object>("dbo.Permission_Verify", p =>
        {
            p.AddWithValue("@UserId", userId);
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

    public async Task<IReadOnlyCollection<PermissionResponse>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Permission_GetAll",
            null,
            r => new PermissionResponse(
                r.GetString(r.GetOrdinal("Slug")),
                r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description"))
            ),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Role_GetAll",
            null,
            r => new RoleResponse(
                r.GetInt32(r.GetOrdinal("Id")),
                r.GetString(r.GetOrdinal("Name")),
                r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description"))
            ),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<GroupResponse>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Group_GetAll",
            null,
            r => new GroupResponse(
                r.GetInt32(r.GetOrdinal("Id")),
                r.GetString(r.GetOrdinal("Name")),
                r.IsDBNull(r.GetOrdinal("ParentGroupId")) ? null : r.GetInt32(r.GetOrdinal("ParentGroupId"))
            ),
            cancellationToken
        );
    }

    public async Task InsertRoleAsync(string name, string description, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Role_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", description ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task InsertGroupAsync(string name, int? parentGroupId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Group_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@ParentGroupId", parentGroupId ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task InsertUserGroupAsync(int userId, int groupId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserGroup_Insert", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@GroupId", groupId);
        }, cancellationToken);
    }

    public async Task InsertPermissionAsync(string slug, string description, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.Permission_Create", p =>
        {
            p.AddWithValue("@Slug", slug);
            p.AddWithValue("@Description", description ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.RolePermission_Grant", p =>
        {
            p.AddWithValue("@RoleId", roleId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
            p.AddWithValue("@IsDeny", isDeny);
        }, cancellationToken);
    }

    public async Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.UserPermission_Grant", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
            p.AddWithValue("@IsDeny", isDeny);
        }, cancellationToken);
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
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
        };
    }

    private static RefreshToken MapRefreshToken(SqlDataReader reader)
    {
        return new RefreshToken
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
            Token = reader.GetString(reader.GetOrdinal("Token")),
            Expires = reader.GetDateTime(reader.GetOrdinal("Expires")),
            Revoked = reader.IsDBNull(reader.GetOrdinal("Revoked")) ? null : reader.GetDateTime(reader.GetOrdinal("Revoked")),
            ReplacedByToken = reader.IsDBNull(reader.GetOrdinal("ReplacedByToken")) ? null : reader.GetString(reader.GetOrdinal("ReplacedByToken"))
        };
    }
}