using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The procedures behind per-account lockout, changing a password while signed in, and the session
/// list. Each test makes its own user, so they share the database with everything else safely.
/// </summary>
[Collection("SqlEdge")]
public sealed class AccountSecurityProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    [SkippableFact]
    public async Task The_failure_that_reaches_the_limit_locks_the_account_and_restarts_the_count()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();

        Assert.Null(await RecordFailureAsync(userId, maxFailures: 3));
        Assert.Null(await RecordFailureAsync(userId, maxFailures: 3));
        DateTime? lockedUntil = await RecordFailureAsync(userId, maxFailures: 3);

        Assert.NotNull(lockedUntil);
        Assert.True(lockedUntil > DateTime.UtcNow.AddSeconds(500));
        Assert.Equal(0, await ScalarAsync<int>("SELECT FailedLoginCount FROM dbo.Users WHERE Id = @p0", userId));
    }

    [SkippableFact]
    public async Task Concurrent_failures_are_each_counted()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RecordFailureAsync(userId, maxFailures: 100)));

        Assert.Equal(8, await ScalarAsync<int>("SELECT FailedLoginCount FROM dbo.Users WHERE Id = @p0", userId));
    }

    [SkippableFact]
    public async Task A_successful_sign_in_clears_the_count_and_stamps_the_time()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        await RecordFailureAsync(userId, maxFailures: 10);

        await ProcAsync("dbo.User_RecordLoginSuccess", ("@UserId", userId));

        Assert.Equal(0, await ScalarAsync<int>("SELECT FailedLoginCount FROM dbo.Users WHERE Id = @p0", userId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Users WHERE Id = @p0 AND LastLoginAt IS NOT NULL", userId));
    }

    [SkippableFact]
    public async Task Changing_the_password_revokes_every_session_and_lifts_a_lock()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        await AddSessionAsync(userId);
        await AddSessionAsync(userId);
        await RecordFailureAsync(userId, maxFailures: 1);

        await ProcAsync("dbo.User_ChangePassword", ("@UserId", userId), ("@PasswordHash", "new-hash"), ("@RevokedByIp", "10.0.0.1"));

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND Revoked IS NULL", userId));
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Users
            WHERE Id = @p0 AND PasswordHash = N'new-hash' AND LockedUntil IS NULL AND PasswordChangedAt IS NOT NULL
            """, userId));
    }

    [SkippableFact]
    public async Task Sessions_list_only_live_ones_and_revoke_only_the_owners()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        int otherUserId = await CreateUserAsync();
        Guid live = await AddSessionAsync(userId);
        Guid expired = await AddSessionAsync(userId, expires: "2000-01-01");
        Guid revoked = await AddSessionAsync(userId);
        await ExecAsync("UPDATE dbo.RefreshTokens SET Revoked = SYSUTCDATETIME() WHERE SessionId = @p0", revoked);

        List<Guid> listed = await ListSessionsAsync(userId);
        Assert.Equal([live], listed);

        // Another user's id, an expired one and an already-revoked one all revoke nothing.
        Assert.Equal(0, await RevokeAsync(live, otherUserId));
        Assert.Equal(0, await RevokeAsync(expired, userId));
        Assert.Equal(0, await RevokeAsync(revoked, userId));

        Assert.Equal(1, await RevokeAsync(live, userId));
        Assert.Empty(await ListSessionsAsync(userId));
    }

    // ---------------------------------------------------------------- helpers

    private Task<int> CreateUserAsync() =>
        ScalarAsync<int>("""
            DECLARE @u NVARCHAR(40) = CONCAT('acct-', LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 20));
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id
            VALUES (@u, CONCAT(@u, '@example.test'), 'hash', 1);
            """);

    private Task<Guid> AddSessionAsync(int userId, string expires = "2999-01-01") =>
        ScalarAsync<Guid>("""
            INSERT INTO dbo.RefreshTokens (UserId, TokenHash, Expires, CreatedByIp) OUTPUT INSERTED.SessionId
            VALUES (@p0, CRYPT_GEN_RANDOM(32), CONVERT(DATETIME2(3), @p1), N'10.0.0.1');
            """, userId, expires);

    private async Task<DateTime?> RecordFailureAsync(int userId, int maxFailures)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.User_RecordLoginFailure", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@MaxFailures", maxFailures);
        cmd.Parameters.AddWithValue("@LockoutSeconds", 900);
        object? result = await cmd.ExecuteScalarAsync();
        return result is DateTime value ? value : null;
    }

    private async Task<List<Guid>> ListSessionsAsync(int userId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.RefreshToken_GetActiveForUser", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(reader.GetOrdinal("SessionId")));
        }

        return ids;
    }

    private async Task<int> RevokeAsync(Guid sessionId, int userId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.RefreshToken_RevokeForUser", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@UserId", userId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task ProcAsync(string procedure, params (string Name, object Value)[] parameters)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Command(sql, conn, args);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Command(sql, conn, args);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static SqlCommand Command(string sql, SqlConnection conn, object[] args)
    {
        var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return cmd;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
