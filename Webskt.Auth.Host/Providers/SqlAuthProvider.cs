using System.Data;
using Microsoft.Data.SqlClient;
using Webskt.Auth.Host.Models;
using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Data;

namespace Webskt.Auth.Host.Providers;

internal class SqlAuthProvider : BaseSqlProvider, IAuthProvider
{
    public SqlAuthProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> FindByReferenceIdAsync(Guid referenceId)
    {
        var result = await ExecuteScalarAsync<int>("dbo.User_FindBy_RefId", p =>
        {
            p.AddWithValue("@RefId", referenceId);
        });

        return result;
    }

    public async Task<User> GetByEmailAsync(string email)
    {
        return await ExecuteSingleAsync(
            "dbo.User_GetBy_Email",
            p => p.AddWithValue("@Email", email),
            MapUser,
            new SecurityException($"User with email {email} not found.")
        );
    }

    public async Task<User> GetByIdAsync(int id)
    {
        return await ExecuteSingleAsync(
            "dbo.User_GetBy_Id",
            p => p.AddWithValue("@Id", id),
            MapUser,
            new SecurityException($"User with id {id} not found.")
        );
    }

    public async Task<int> InsertUserAsync(User user)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.User_Create", p =>
        {
            p.AddWithValue("@Username", user.Username);
            p.AddWithValue("@Email", user.Email);
            p.AddWithValue("@PasswordHash", user.PasswordHash);
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        });

        return (int)parameters["@Id"].Value;
    }

    public async Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.RefreshToken_Insert", p =>
        {
            p.AddWithValue("@UserId", token.UserId);
            p.AddWithValue("@Token", token.Token);
            p.AddWithValue("@Expires", token.Expires);
            p.AddWithValue("@CreatedByIp", ipAddress ?? (object)DBNull.Value);
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        });

        token.Id = (int)parameters["@Id"].Value;
    }

    public async Task<RefreshToken> GetRefreshTokenAsync(string token)
    {
        return await ExecuteSingleAsync(
            "dbo.RefreshToken_GetBy_Token",
            p => p.AddWithValue("@Token", token),
            MapRefreshToken,
            new SecurityException("Invalid refresh token.")
        );
    }

    public async Task<bool> VerifyPermissionAsync(int userId, string permissionSlug)
    {
        var result = await ExecuteScalarAsync<object>("dbo.Permission_Verify", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
        });

        return result switch
        {
            null => false,
            bool b => b,
            int i => i == 1,
            _ => false
        };
    }

    public async Task<IReadOnlyCollection<PermissionResponse>> GetPermissionsAsync()
    {
        return await ExecuteCollectionAsync(
            "dbo.Permission_GetAll",
            null,
            r => new PermissionResponse(
                r.GetString(r.GetOrdinal("Slug")),
                r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description"))
            )
        );
    }

    public async Task<IReadOnlyCollection<RoleResponse>> GetRolesAsync()
    {
        return await ExecuteCollectionAsync(
            "dbo.Role_GetAll",
            null,
            r => new RoleResponse(
                r.GetInt32(r.GetOrdinal("Id")),
                r.GetString(r.GetOrdinal("Name")),
                r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description"))
            )
        );
    }

    public async Task<IReadOnlyCollection<GroupResponse>> GetGroupsAsync()
    {
        return await ExecuteCollectionAsync(
            "dbo.Group_GetAll",
            null,
            r => new GroupResponse(
                r.GetInt32(r.GetOrdinal("Id")),
                r.GetString(r.GetOrdinal("Name")),
                r.IsDBNull(r.GetOrdinal("ParentGroupId")) ? null : r.GetInt32(r.GetOrdinal("ParentGroupId"))
            )
        );
    }

    public async Task InsertRoleAsync(string name, string description)
    {
        await ExecuteNonQueryAsync("dbo.Role_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@Description", description ?? (object)DBNull.Value);
        });
    }

    public async Task InsertGroupAsync(string name, int? parentGroupId)
    {
        await ExecuteNonQueryAsync("dbo.Group_Create", p =>
        {
            p.AddWithValue("@Name", name);
            p.AddWithValue("@ParentGroupId", parentGroupId ?? (object)DBNull.Value);
        });
    }

    public async Task InsertUserGroupAsync(int userId, int groupId)
    {
        await ExecuteNonQueryAsync("dbo.UserGroup_Insert", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@GroupId", groupId);
        });
    }

    public async Task InsertPermissionAsync(string slug, string description)
    {
        await ExecuteNonQueryAsync("dbo.Permission_Create", p =>
        {
            p.AddWithValue("@Slug", slug);
            p.AddWithValue("@Description", description ?? (object)DBNull.Value);
        });
    }

    public async Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny)
    {
        await ExecuteNonQueryAsync("dbo.RolePermission_Grant", p =>
        {
            p.AddWithValue("@RoleId", roleId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
            p.AddWithValue("@IsDeny", isDeny);
        });
    }

    public async Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny)
    {
        await ExecuteNonQueryAsync("dbo.UserPermission_Grant", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@PermissionSlug", permissionSlug);
            p.AddWithValue("@IsDeny", isDeny);
        });
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