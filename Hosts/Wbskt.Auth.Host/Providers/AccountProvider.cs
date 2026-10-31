using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Providers;

internal sealed class AccountProvider : BaseSqlProvider, IAccountProvider
{
    public AccountProvider(IConfiguration configuration) : base(configuration, "AuthDBConnection") { }

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
            // No address in the message: whatever lets this escape logs it.
            new SecurityException("No user with that email address."),
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

    public async Task<DateTime?> RecordLoginFailureAsync(int userId, int maxFailures, TimeSpan lockout, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<DateTime?>("dbo.User_RecordLoginFailure", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@MaxFailures", maxFailures);
            p.AddWithValue("@LockoutSeconds", (int)lockout.TotalSeconds);
        }, cancellationToken);
    }

    public async Task RecordLoginSuccessAsync(int userId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.User_RecordLoginSuccess", p => p.AddWithValue("@UserId", userId), cancellationToken);
    }

    public async Task<bool> UpgradePasswordHashAsync(int userId, string currentPasswordHash, string newPasswordHash, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.User_UpgradePasswordHash", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@CurrentPasswordHash", currentPasswordHash);
            p.AddWithValue("@NewPasswordHash", newPasswordHash);
        }, cancellationToken) == 1;
    }

    public async Task ChangePasswordAsync(int userId, string passwordHash, string? revokedByIp, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.User_ChangePassword", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@PasswordHash", passwordHash);
            p.AddWithValue("@RevokedByIp", (object?)revokedByIp ?? DBNull.Value);
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

    public async Task<IReadOnlyCollection<int>> GetSoleTenantWorkspaceIdsAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.User_GetSoleTenantWorkspaces",
            p => p.AddWithValue("@UserId", userId),
            reader => reader.GetInt32(0),
            cancellationToken);
    }

    public async Task DeleteUserAsync(int userId, IReadOnlyDictionary<int, OutboxMessage> deletedWorkspaces, CancellationToken cancellationToken = default)
    {
        // The property names are what the procedure's OPENJSON reads.
        var planned = JsonSerializer.Serialize(deletedWorkspaces.Select(w => new Dictionary<string, object>
        {
            ["workspaceId"] = w.Key,
            ["messageType"] = w.Value.MessageType,
            ["payload"] = w.Value.Payload
        }));

        await ExecuteNonQueryAsync("dbo.User_Delete", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.Add("@DeletedWorkspaces", SqlDbType.NVarChar, -1).Value = planned;
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
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            IsEmailVerified = reader.GetBoolean(reader.GetOrdinal("IsEmailVerified")),
            LockedUntil = reader.IsDBNull(reader.GetOrdinal("LockedUntil")) ? null : reader.GetDateTime(reader.GetOrdinal("LockedUntil"))
        };
    }
}
