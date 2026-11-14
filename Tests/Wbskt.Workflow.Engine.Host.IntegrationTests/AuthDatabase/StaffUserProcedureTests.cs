using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The writes staff make to a person's account: dbo.User_ClearLockout lifts a lock without stamping a
/// sign-in, and dbo.User_StaffSetActive turns sign-in off with every session, or back on.
/// </summary>
[Collection("SqlEdge")]
public sealed class StaffUserProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    [SkippableFact]
    public async Task Clearing_a_lockout_resets_the_count_and_leaves_the_last_sign_in_alone()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        await ExecuteAsync("UPDATE dbo.Users SET FailedLoginCount = 4, LockedUntil = DATEADD(minute, 15, SYSUTCDATETIME()) WHERE Id = @p0", userId);

        await ProcAsync("dbo.User_ClearLockout", ("@Id", userId));

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Users WHERE Id = @p0 AND FailedLoginCount = 0 AND LockedUntil IS NULL AND LastLoginAt IS NULL", userId));
    }

    [SkippableFact]
    public async Task Turning_sign_in_off_revokes_every_live_session()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        await AddSessionAsync(userId);
        await AddSessionAsync(userId);

        await ProcAsync("dbo.User_StaffSetActive", ("@Id", userId), ("@IsActive", false), ("@RevokedByIp", "staff"));

        Assert.False(await ScalarAsync<bool>("SELECT IsActive FROM dbo.Users WHERE Id = @p0", userId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND Revoked IS NULL", userId));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND RevokedByIp = N'staff'", userId));
    }

    [SkippableFact]
    public async Task Turning_sign_in_on_leaves_sessions_as_they_are()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        await ExecuteAsync("UPDATE dbo.Users SET IsActive = 0 WHERE Id = @p0", userId);
        await AddSessionAsync(userId);

        await ProcAsync("dbo.User_StaffSetActive", ("@Id", userId), ("@IsActive", true), ("@RevokedByIp", "staff"));

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND Revoked IS NULL", userId));
    }

    private async Task AddSessionAsync(int userId) =>
        await ExecuteAsync("INSERT INTO dbo.RefreshTokens (UserId, TokenHash, Expires) VALUES (@p0, CRYPT_GEN_RANDOM(32), DATEADD(day, 7, SYSUTCDATETIME()));", userId);

    private async Task<int> CreateUserAsync()
    {
        string unique = $"staff-{Guid.NewGuid():N}";
        return await ScalarAsync<int>(
            "INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id VALUES (@p0, @p1, 'hash', 1);",
            unique[..30], $"{unique}@example.test");
    }

    private async Task ProcAsync(string name, params (string Name, object Value)[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(name, conn) { CommandType = CommandType.StoredProcedure };
        foreach (var (argName, value) in args)
        {
            cmd.Parameters.AddWithValue(argName, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAsync(string sql, params object[] args)
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
