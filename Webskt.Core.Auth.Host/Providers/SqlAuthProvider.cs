using System.Data;
using Microsoft.Data.SqlClient;
using Webskt.Core.Auth.Host.Models;

namespace Webskt.Core.Auth.Host.Providers;

public class SqlAuthProvider : IAuthProvider
{
    private readonly string _connectionString;

    public SqlAuthProvider(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
                            ?? throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
    }

    public async Task<User> GetByEmailAsync(string email)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("User_GetBy_Email", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Email", email);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Username = reader.GetString(reader.GetOrdinal("Username")),
                Email = reader.GetString(reader.GetOrdinal("Email")),
                PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
            };
        }

        throw new SecurityException($"User with email {email} not found.");
    }

    public async Task<User> GetByIdAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("User_GetBy_Id", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Id", id);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Username = reader.GetString(reader.GetOrdinal("Username")),
                Email = reader.GetString(reader.GetOrdinal("Email")),
                PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
            };
        }

        throw new SecurityException($"User with id {id} not found.");
    }

    public async Task<int> InsertUserAsync(User user)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("User_Create", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Username", user.Username);
        command.Parameters.AddWithValue("@Email", user.Email);
        command.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        
        var outputId = new SqlParameter("@Id", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(outputId);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();

        return (int)outputId.Value;
    }

    public async Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("RefreshToken_Insert", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserId", token.UserId);
        command.Parameters.AddWithValue("@Token", token.Token);
        command.Parameters.AddWithValue("@Expires", token.Expires);
        command.Parameters.AddWithValue("@CreatedByIp", ipAddress ?? (object)DBNull.Value);
        
        var outputId = new SqlParameter("@Id", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(outputId);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
        
        token.Id = (int)outputId.Value;
    }

    public async Task<RefreshToken> GetRefreshTokenAsync(string token)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("RefreshToken_GetBy_Token", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Token", token);

        await connection.OpenAsync();
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
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

        throw new SecurityException("Invalid refresh token.");
    }

    public async Task<bool> VerifyPermissionAsync(int userId, string permissionSlug)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("Permission_Verify", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@PermissionSlug", permissionSlug);

        await connection.OpenAsync();
        var result = await command.ExecuteScalarAsync();
        
        if (result != null && result != DBNull.Value)
        {
            // Logic requiring an empty line before this comment
            if (result is bool b)
            {
                return b;
            }

            if (result is int i)
            {
                return i == 1;
            }
        }

        return false;
    }

    public async Task InsertRoleAsync(string name, string description)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("Role_Create", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Description", description ?? (object)DBNull.Value);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task InsertGroupAsync(string name, int? parentGroupId)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("Group_Create", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@ParentGroupId", parentGroupId ?? (object)DBNull.Value);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task InsertUserGroupAsync(int userId, int groupId)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("UserGroup_Insert", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@GroupId", groupId);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task InsertPermissionAsync(string slug, string description)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("Permission_Create", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Slug", slug);
        command.Parameters.AddWithValue("@Description", description ?? (object)DBNull.Value);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("RolePermission_Grant", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@PermissionSlug", permissionSlug);
        command.Parameters.AddWithValue("@IsDeny", isDeny);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public async Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("UserPermission_Grant", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@PermissionSlug", permissionSlug);
        command.Parameters.AddWithValue("@IsDeny", isDeny);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }
}