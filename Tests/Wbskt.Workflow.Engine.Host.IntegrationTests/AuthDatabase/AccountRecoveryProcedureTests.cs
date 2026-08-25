using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The four account-recovery procedures, against a real SQL Server.
///
/// These exist because the guarantees the feature is sold on are enforced here and nowhere else.
/// Single use, expiry, supersession, and "a reset revokes every session" are all properties of the
/// T-SQL — the unit suite mocks the provider, so it can only assert that the service *calls* these,
/// never that they do what their names say. A procedure that consumed a token twice, or wrote the
/// password without revoking anything, would pass every test in the other project.
/// </summary>
[Collection("SqlEdge")]
public sealed class AccountRecoveryProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    /// <summary>
    /// A fresh 32-byte hash per call. Not a constant shared between tests: UQ_*_TokenHash is
    /// table-wide rather than filtered — correctly, since a token is looked up by its hash alone and
    /// two rows sharing one would be ambiguous — and every test in this class runs against the same
    /// database, so a reused value collides with whichever test ran first.
    /// </summary>
    private static byte[] NewTokenHash() =>
        System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray());

    // ---------------------------------------------------------------- password reset

    [SkippableFact]
    public async Task A_reset_writes_the_password_and_revokes_every_session()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] token = NewTokenHash();
        int userId = await SeedUserAsync("reset-happy");
        int otherId = await SeedUserAsync("reset-bystander");
        await SeedRefreshTokenAsync(userId, "live-1");
        await SeedRefreshTokenAsync(userId, "live-2");
        await SeedRefreshTokenAsync(otherId, "someone-elses");

        await CreateResetTokenAsync(userId, token);
        int consumedFor = await ConsumeResetTokenAsync(token, "the-new-hash");

        Assert.Equal(userId, consumedFor);
        Assert.Equal("the-new-hash", await ScalarAsync<string>("SELECT PasswordHash FROM dbo.Users WHERE Id = @p0", userId));

        // The assertion the unit suite structurally cannot make. Both of this user's sessions are
        // gone; recovering an account is usually a response to losing control of it, and a session
        // the attacker already holds must not outlive the reset.
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND Revoked IS NULL", userId));

        // And nobody else's.
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0 AND Revoked IS NULL", otherId));
    }

    [SkippableFact]
    public async Task A_reset_token_cannot_be_redeemed_twice()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] token = NewTokenHash();
        int userId = await SeedUserAsync("reset-single-use");
        await CreateResetTokenAsync(userId, token);

        await ConsumeResetTokenAsync(token, "first-hash");

        var ex = await Assert.ThrowsAsync<SqlException>(() => ConsumeResetTokenAsync(token, "second-hash"));
        Assert.Equal(50014, ex.Number);

        // The second attempt changed nothing.
        Assert.Equal("first-hash", await ScalarAsync<string>("SELECT PasswordHash FROM dbo.Users WHERE Id = @p0", userId));
    }

    [SkippableFact]
    public async Task An_expired_reset_token_is_refused()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] token = NewTokenHash();
        int userId = await SeedUserAsync("reset-expired");
        await CreateResetTokenAsync(userId, token, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var ex = await Assert.ThrowsAsync<SqlException>(() => ConsumeResetTokenAsync(token, "hash"));
        Assert.Equal(50014, ex.Number);
    }

    /// <summary>
    /// Asking again is the normal way to replace a link that was lost. It must supersede rather than
    /// collide with UX_PasswordResetTokens_Outstanding — a second request that threw would make
    /// forgot-password work exactly once per account.
    /// </summary>
    [SkippableFact]
    public async Task Requesting_a_second_reset_supersedes_the_first()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] first = NewTokenHash();
        byte[] second = NewTokenHash();
        int userId = await SeedUserAsync("reset-supersede");
        await CreateResetTokenAsync(userId, first);
        await CreateResetTokenAsync(userId, second);

        var ex = await Assert.ThrowsAsync<SqlException>(() => ConsumeResetTokenAsync(first, "from-the-old-link"));
        Assert.Equal(50014, ex.Number);

        Assert.Equal(userId, await ConsumeResetTokenAsync(second, "from-the-new-link"));
    }

    /// <summary>
    /// The UPDLOCK/HOLDLOCK in the procedure exists for exactly this. Two redemptions of one token
    /// arriving together must not both pass the <c>ConsumedAt IS NULL</c> test.
    /// </summary>
    [SkippableFact]
    public async Task Two_simultaneous_redemptions_produce_exactly_one_winner()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] token = NewTokenHash();
        int userId = await SeedUserAsync("reset-race");
        await CreateResetTokenAsync(userId, token);

        var results = await Task.WhenAll(
            AttemptAsync(() => ConsumeResetTokenAsync(token, "hash-one")),
            AttemptAsync(() => ConsumeResetTokenAsync(token, "hash-two")));

        Assert.Equal(1, results.Count(r => r));
    }

    // ---------------------------------------------------------------- email verification

    [SkippableFact]
    public async Task Consuming_a_verification_token_marks_the_address_confirmed()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] token = NewTokenHash();
        int userId = await SeedUserAsync("verify-happy");
        Assert.False(await ScalarAsync<bool>("SELECT IsEmailVerified FROM dbo.Users WHERE Id = @p0", userId));

        await CreateVerificationTokenAsync(userId, token);
        Assert.Equal(userId, await ConsumeVerificationTokenAsync(token));

        Assert.True(await ScalarAsync<bool>("SELECT IsEmailVerified FROM dbo.Users WHERE Id = @p0", userId));
    }

    [SkippableFact]
    public async Task A_verification_token_cannot_be_redeemed_twice()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] token = NewTokenHash();
        int userId = await SeedUserAsync("verify-single-use");
        await CreateVerificationTokenAsync(userId, token);
        await ConsumeVerificationTokenAsync(token);

        var ex = await Assert.ThrowsAsync<SqlException>(() => ConsumeVerificationTokenAsync(token));
        Assert.Equal(50015, ex.Number);
    }

    [SkippableFact]
    public async Task An_expired_verification_token_is_refused()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] token = NewTokenHash();
        int userId = await SeedUserAsync("verify-expired");
        await CreateVerificationTokenAsync(userId, token, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var ex = await Assert.ThrowsAsync<SqlException>(() => ConsumeVerificationTokenAsync(token));
        Assert.Equal(50015, ex.Number);

        Assert.False(await ScalarAsync<bool>("SELECT IsEmailVerified FROM dbo.Users WHERE Id = @p0", userId));
    }

    [SkippableFact]
    public async Task Resending_verification_supersedes_the_previous_link()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        byte[] first = NewTokenHash();
        byte[] second = NewTokenHash();
        int userId = await SeedUserAsync("verify-supersede");
        await CreateVerificationTokenAsync(userId, first);
        await CreateVerificationTokenAsync(userId, second);

        var ex = await Assert.ThrowsAsync<SqlException>(() => ConsumeVerificationTokenAsync(first));
        Assert.Equal(50015, ex.Number);

        Assert.Equal(userId, await ConsumeVerificationTokenAsync(second));
    }

    /// <summary>
    /// A new account starts unverified. If the column's default were 1, every account would be
    /// verified on creation and the whole feature would be a no-op that no other test would catch.
    /// </summary>
    [SkippableFact]
    public async Task User_Create_leaves_a_new_account_unverified()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.User_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Username", $"proc-created-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("@Email", $"{Guid.NewGuid():N}@example.test");
        cmd.Parameters.AddWithValue("@PasswordHash", "hash");
        cmd.Parameters.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        int id = (int)cmd.Parameters["@Id"].Value;
        Assert.False(await ScalarAsync<bool>("SELECT IsEmailVerified FROM dbo.Users WHERE Id = @p0", id));
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<bool> AttemptAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task<int> SeedUserAsync(string tag)
    {
        // Users.Username is NVARCHAR(50); truncate defensively rather than relying on every
        // caller's tag being short enough to make a fixed-length slice legal.
        string candidate = $"{tag}-{Guid.NewGuid():N}";
        string unique = candidate.Length <= 40 ? candidate : candidate[..40];

        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(
            """
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@Username, @Email, 'initial-hash', 1);
            """, conn);
        cmd.Parameters.AddWithValue("@Username", unique);
        cmd.Parameters.AddWithValue("@Email", $"{unique}@example.test");

        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task SeedRefreshTokenAsync(int userId, string token)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(
            """
            INSERT INTO dbo.RefreshTokens (UserId, Token, Expires)
            VALUES (@UserId, @Token, DATEADD(day, 7, SYSUTCDATETIME()));
            """, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@Token", $"{token}-{Guid.NewGuid():N}");
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CreateResetTokenAsync(int userId, byte[] tokenHash, DateTime? expiresAt = null)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.PasswordResetToken_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@ExpiresAt", expiresAt ?? DateTime.UtcNow.AddHours(1));
        cmd.Parameters.AddWithValue("@RequestedByIp", "10.0.0.1");
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> ConsumeResetTokenAsync(byte[] tokenHash, string passwordHash)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.PasswordResetToken_Consume", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
        cmd.Parameters.AddWithValue("@RevokedByIp", "10.0.0.1");
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        return (int)cmd.Parameters["@UserId"].Value;
    }

    private async Task CreateVerificationTokenAsync(int userId, byte[] tokenHash, DateTime? expiresAt = null)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.EmailVerificationToken_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@ExpiresAt", expiresAt ?? DateTime.UtcNow.AddHours(24));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> ConsumeVerificationTokenAsync(byte[] tokenHash)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.EmailVerificationToken_Consume", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        return (int)cmd.Parameters["@UserId"].Value;
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
