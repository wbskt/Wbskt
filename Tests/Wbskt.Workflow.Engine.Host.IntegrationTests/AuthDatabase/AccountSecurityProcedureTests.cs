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
    public async Task A_locked_account_is_refused_a_check_unless_it_goes_through_the_lock()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        await ExecAsync("UPDATE dbo.Users SET LockedUntil = DATEADD(minute, 15, SYSUTCDATETIME()) WHERE Id = @p0", userId);

        Assert.False(await BeginAsync(userId, maxFailures: 3));
        Assert.True(await BeginAsync(userId, maxFailures: 3, throughLock: true));
    }

    [SkippableFact]
    public async Task Concurrent_checks_get_no_more_slots_than_the_limit()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();

        bool[] reserved = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => BeginAsync(userId, maxFailures: 5)));

        Assert.Equal(5, reserved.Count(r => r));
        Assert.Equal(5, await ScalarAsync<int>("SELECT FailedLoginCount FROM dbo.Users WHERE Id = @p0", userId));
    }

    [SkippableFact]
    public async Task A_right_password_gives_its_slot_back()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        await RecordFailureAsync(userId, maxFailures: 10);
        Assert.True(await BeginAsync(userId, maxFailures: 10));

        Assert.Null(await EndAsync(userId, succeeded: true, maxFailures: 10));

        Assert.Equal(1, await ScalarAsync<int>("SELECT FailedLoginCount FROM dbo.Users WHERE Id = @p0", userId));
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
    public async Task A_hash_upgrade_applies_only_while_the_old_hash_is_still_stored()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        string stored = await ScalarAsync<string>("SELECT PasswordHash FROM dbo.Users WHERE Id = @p0", userId);

        Assert.Equal(1, await UpgradeAsync(userId, stored, "upgraded-hash"));
        // A second sign-in still holding the old hash, or a password change that landed first, wins.
        Assert.Equal(0, await UpgradeAsync(userId, stored, "late-hash"));
        Assert.Equal("upgraded-hash", await ScalarAsync<string>("SELECT PasswordHash FROM dbo.Users WHERE Id = @p0", userId));
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

    [SkippableFact]
    public async Task A_recorded_device_is_known_only_to_its_own_account_and_only_while_live()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        int otherId = await CreateUserAsync();
        byte[] live = RandomHash();
        byte[] lapsed = RandomHash();

        await AddDeviceAsync(userId, live, DateTime.UtcNow.AddDays(1));
        await ExecAsync("INSERT INTO dbo.UserDevices (UserId, TokenHash, ExpiresAt) VALUES (@p0, @p1, DATEADD(DAY, -1, SYSUTCDATETIME()))", userId, lapsed);

        Assert.True(await IsKnownDeviceAsync(userId, live));
        Assert.False(await IsKnownDeviceAsync(otherId, live));
        Assert.False(await IsKnownDeviceAsync(userId, lapsed));
        Assert.False(await IsKnownDeviceAsync(userId, RandomHash()));
    }

    [SkippableFact]
    public async Task Recording_a_device_keeps_only_the_newest_and_drops_lapsed_ones()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        await ExecAsync("INSERT INTO dbo.UserDevices (UserId, TokenHash, ExpiresAt) VALUES (@p0, CRYPT_GEN_RANDOM(32), DATEADD(DAY, -1, SYSUTCDATETIME()))", userId);
        var hashes = Enumerable.Range(0, 4).Select(_ => RandomHash()).ToList();

        foreach (var hash in hashes)
        {
            await AddDeviceAsync(userId, hash, DateTime.UtcNow.AddDays(1), maxPerUser: 3);
        }

        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.UserDevices WHERE UserId = @p0", userId));
        Assert.False(await IsKnownDeviceAsync(userId, hashes[0]));
        Assert.True(await IsKnownDeviceAsync(userId, hashes[3]));
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

    /// <summary>A wrong password, as the service records one: reserved, then settled as a failure.</summary>
    private async Task<DateTime?> RecordFailureAsync(int userId, int maxFailures)
    {
        Assert.True(await BeginAsync(userId, maxFailures));
        return await EndAsync(userId, succeeded: false, maxFailures);
    }

    private async Task<bool> BeginAsync(int userId, int maxFailures, bool throughLock = false)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.User_BeginPasswordCheck", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@MaxFailures", maxFailures);
        cmd.Parameters.AddWithValue("@ThroughLock", throughLock);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<DateTime?> EndAsync(int userId, bool succeeded, int maxFailures)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.User_EndPasswordCheck", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@Succeeded", succeeded);
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

    private async Task<int> UpgradeAsync(int userId, string currentHash, string newHash)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.User_UpgradePasswordHash", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@CurrentPasswordHash", currentHash);
        cmd.Parameters.AddWithValue("@NewPasswordHash", newHash);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> RevokeAsync(Guid sessionId, int userId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.RefreshToken_RevokeForUser", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@UserId", userId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static byte[] RandomHash() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    private Task AddDeviceAsync(int userId, byte[] hash, DateTime expiresAt, int maxPerUser = 10) =>
        ProcAsync("dbo.UserDevice_Create", ("@UserId", userId), ("@TokenHash", hash), ("@ExpiresAt", expiresAt), ("@MaxPerUser", maxPerUser));

    private async Task<bool> IsKnownDeviceAsync(int userId, byte[] hash)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.UserDevice_Verify", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@TokenHash", hash);
        return (bool)(await cmd.ExecuteScalarAsync())!;
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
