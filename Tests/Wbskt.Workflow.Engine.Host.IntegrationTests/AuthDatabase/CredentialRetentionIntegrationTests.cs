using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// <c>dbo.Credential_DeleteExpired</c>. Rows expire in 2000 and the cutoff is 2001, so the shared
/// database's other rows, which all expire in the future, are never swept by this test.
/// </summary>
[Collection("SqlEdge")]
public sealed class CredentialRetentionIntegrationTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";
    private static readonly DateTime Cutoff = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Sweeps_credentials_that_expired_before_the_cutoff_and_keeps_the_rest()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await ScalarAsync<int>("""
            DECLARE @u NVARCHAR(40) = CONCAT('retention-', LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 20));
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id
            VALUES (@u, CONCAT(@u, '@example.test'), 'hash', 1);
            """);
        int tenantId = await ScalarAsync<int>("INSERT INTO dbo.Tenants (RefId, Name) OUTPUT INSERTED.Id VALUES (NEWID(), N'retention');");

        await ExecAsync("""
            INSERT INTO dbo.RefreshTokens (UserId, TokenHash, Expires)
            VALUES (@p0, CRYPT_GEN_RANDOM(32), '2000-01-01'), (@p0, CRYPT_GEN_RANDOM(32), '2001-06-01');
            INSERT INTO dbo.PasswordResetTokens (UserId, TokenHash, ExpiresAt) VALUES (@p0, CRYPT_GEN_RANDOM(32), '2000-01-01');
            INSERT INTO dbo.EmailVerificationTokens (UserId, TokenHash, ExpiresAt) VALUES (@p0, CRYPT_GEN_RANDOM(32), '2000-01-01');
            INSERT INTO dbo.TenantInvitations (TenantId, Email, TokenHash, ExpiresAt, InvitedByUserId)
            VALUES (@p1, N'lapsed@example.test', CRYPT_GEN_RANDOM(32), '2000-01-01', @p0);
            INSERT INTO dbo.TenantInvitations (TenantId, Email, TokenHash, ExpiresAt, AcceptedAt, AcceptedByUserId, InvitedByUserId)
            VALUES (@p1, N'joined@example.test', CRYPT_GEN_RANDOM(32), '2000-01-01', '1999-12-31', @p0, @p0);
            """, userId, tenantId);

        int total = 0, deleted;
        while ((deleted = await DeleteExpiredAsync()) > 0)
        {
            total += deleted;
        }

        Assert.True(total >= 4);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RefreshTokens WHERE UserId = @p0", userId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PasswordResetTokens WHERE UserId = @p0", userId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EmailVerificationTokens WHERE UserId = @p0", userId));

        // An accepted invitation is the record of how someone joined, so it stays.
        Assert.Equal("joined@example.test", await ScalarAsync<string>("SELECT Email FROM dbo.TenantInvitations WHERE TenantId = @p0", tenantId));
    }

    private async Task<int> DeleteExpiredAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Credential_DeleteExpired", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@CutoffUtc", SqlDbType.DateTime2).Value = Cutoff;
        cmd.Parameters.AddWithValue("@BatchSize", 1);
        return (int)(await cmd.ExecuteScalarAsync())!;
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
