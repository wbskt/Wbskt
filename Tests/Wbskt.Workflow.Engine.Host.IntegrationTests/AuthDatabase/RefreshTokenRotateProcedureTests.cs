using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// <c>dbo.RefreshToken_Rotate</c>: one call that retires a refresh token and stores its replacement.
/// The properties that matter are that a rotation is all-or-nothing, that only a token already
/// retired when it is read counts as a replay (and ends every session), and that two concurrent
/// exchanges of one token produce exactly one replacement.
/// </summary>
[Collection("SqlEdge")]
public sealed class RefreshTokenRotateProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    [SkippableFact]
    public async Task A_live_token_is_retired_and_its_replacement_stored()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        byte[] old = await AddTokenAsync(userId);
        byte[] replacement = NewHash();

        var result = await RotateAsync(old, replacement);

        Assert.Equal("Rotated", result.Outcome);
        Assert.Equal(userId, result.UserId);
        Assert.NotNull(result.Username);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.RefreshTokens WHERE TokenHash = @p0 AND Revoked IS NOT NULL AND ReplacedByTokenHash = @p1", old, replacement));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.RefreshTokens WHERE TokenHash = @p0 AND UserId = @p1 AND Revoked IS NULL", replacement, userId));
    }

    [SkippableFact]
    public async Task An_unknown_token_changes_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        var result = await RotateAsync(NewHash(), NewHash());

        Assert.Equal("Unknown", result.Outcome);
        Assert.Null(result.UserId);
    }

    [SkippableFact]
    public async Task Presenting_a_retired_token_revokes_every_session_the_user_has()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        int otherUserId = await CreateUserAsync();
        byte[] old = await AddTokenAsync(userId);
        await AddTokenAsync(userId);
        await AddTokenAsync(otherUserId);
        byte[] successor = NewHash();
        await RotateAsync(old, successor);

        var replay = await RotateAsync(old, NewHash());

        Assert.Equal("Replayed", replay.Outcome);
        Assert.Equal(userId, replay.UserId);
        Assert.Null(replay.Username);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND Revoked IS NULL", userId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND Revoked IS NULL", otherUserId));
    }

    [SkippableFact]
    public async Task An_expired_token_is_refused_and_left_as_it_was()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        byte[] old = await AddTokenAsync(userId, expires: "2000-01-01");
        byte[] replacement = NewHash();

        var result = await RotateAsync(old, replacement);

        Assert.Equal("Expired", result.Outcome);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE TokenHash = @p0", replacement));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE TokenHash = @p0 AND Revoked IS NULL", old));
    }

    [SkippableFact]
    public async Task A_deactivated_account_cannot_rotate_and_keeps_its_token()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        byte[] old = await AddTokenAsync(userId);
        await ExecAsync("UPDATE dbo.Users SET IsActive = 0 WHERE Id = @p0", userId);
        byte[] replacement = NewHash();

        var result = await RotateAsync(old, replacement);

        Assert.Equal("UserInactive", result.Outcome);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE TokenHash = @p0", replacement));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE TokenHash = @p0 AND Revoked IS NULL", old));
    }

    [SkippableFact]
    public async Task A_failed_insert_leaves_the_presented_token_live()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        byte[] old = await AddTokenAsync(userId);

        // A replacement whose hash already exists violates UQ_RefreshTokens_TokenHash after the revoke
        // has run, which is exactly the half-done state this procedure exists to prevent.
        byte[] collision = await AddTokenAsync(await CreateUserAsync());

        await Assert.ThrowsAsync<SqlException>(() => RotateAsync(old, collision));

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE TokenHash = @p0 AND Revoked IS NULL", old));
    }

    [SkippableFact]
    public async Task Concurrent_exchanges_of_one_token_store_exactly_one_replacement()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        byte[] old = await AddTokenAsync(userId);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RotateAsync(old, NewHash())));

        Assert.Single(results, r => r.Outcome == "Rotated");
        Assert.All(results, r => Assert.Contains(r.Outcome, new[] { "Rotated", "Raced", "Replayed" }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND ReplacedByTokenHash IS NULL AND TokenHash <> @p1", userId, old));
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Rotation(string Outcome, int? UserId, string? Username);

    private static byte[] NewHash() => RandomNumberGenerator.GetBytes(32);

    private async Task<Rotation> RotateAsync(byte[] tokenHash, byte[] newTokenHash)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.RefreshToken_Rotate", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        cmd.Parameters.Add("@NewTokenHash", SqlDbType.VarBinary, 32).Value = newTokenHash;
        cmd.Parameters.AddWithValue("@NewExpires", DateTime.UtcNow.AddDays(7));
        cmd.Parameters.AddWithValue("@Ip", "10.0.0.1");
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new Rotation(
            reader.GetString(reader.GetOrdinal("Outcome")),
            reader.IsDBNull(reader.GetOrdinal("UserId")) ? null : reader.GetInt32(reader.GetOrdinal("UserId")),
            reader.IsDBNull(reader.GetOrdinal("Username")) ? null : reader.GetString(reader.GetOrdinal("Username")));
    }

    private Task<int> CreateUserAsync() =>
        ScalarAsync<int>("""
            DECLARE @u NVARCHAR(40) = CONCAT('rot-', LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 20));
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id
            VALUES (@u, CONCAT(@u, '@example.test'), 'hash', 1);
            """);

    private async Task<byte[]> AddTokenAsync(int userId, string expires = "2999-01-01")
    {
        byte[] hash = NewHash();
        await ExecAsync("""
            INSERT INTO dbo.RefreshTokens (UserId, TokenHash, Expires, CreatedByIp)
            VALUES (@p0, @p1, CONVERT(DATETIME2(3), @p2), N'10.0.0.1');
            """, userId, hash, expires);
        return hash;
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
