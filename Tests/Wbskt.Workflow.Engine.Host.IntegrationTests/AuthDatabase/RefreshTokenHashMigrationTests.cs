using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The <c>RefreshTokens.Token</c> → <c>RefreshTokens.TokenHash</c> migration, through a real
/// sqlpackage publish of the shipped DACPAC with <c>BlockOnPossibleDataLoss</c> on, the way the
/// production migrator runs it.
///
/// What is at stake is every signed-in session: if a carried-over row's hash does not match what
/// the auth host computes for the token the browser holds, every user is signed out at once, and if
/// the publish trips the data-loss block, the deploy fails.
/// </summary>
[Collection("SqlEdge")]
public sealed class RefreshTokenHashMigrationTests
{
    /// <summary>The tables as they were before the change: Users as today, RefreshTokens with plaintext.</summary>
    private const string PreMigrationSchema =
        """
        CREATE TABLE dbo.Users (
            Id           INT              IDENTITY(1, 1) NOT NULL,
            RefId        UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
            Username     NVARCHAR(50)     NOT NULL,
            Email        NVARCHAR(100)    NOT NULL,
            PasswordHash NVARCHAR(255)    NOT NULL,
            IsActive     BIT              NOT NULL           DEFAULT 1,
            IsEmailVerified BIT           NOT NULL           CONSTRAINT DF_Users_IsEmailVerified DEFAULT 0,
            CreatedAt    DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_Users          PRIMARY KEY (Id),
            CONSTRAINT UQ_Users_RefId    UNIQUE (RefId),
            CONSTRAINT UQ_Users_Username UNIQUE (Username),
            CONSTRAINT UQ_Users_Email    UNIQUE (Email)
        );

        CREATE TABLE dbo.RefreshTokens
        (
            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            UserId INT NOT NULL,
            Token NVARCHAR(255) NOT NULL,
            Expires DATETIME2(3) NOT NULL,
            Revoked DATETIME2(3) NULL,
            CreatedByIp NVARCHAR(50) NULL,
            RevokedByIp NVARCHAR(50) NULL,
            ReplacedByToken NVARCHAR(255) NULL,
            CONSTRAINT FK_RefreshTokens_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
            CONSTRAINT UQ_RefreshTokens_Token UNIQUE (Token)
        );
        """;

    [SkippableFact]
    public async Task Existing_sessions_survive_with_hashes_the_auth_host_will_match()
    {
        Skip.IfNot(await AuthSqlFixture.CanConnectToMasterAsync(), "SQL Server not reachable — skipping.");

        string dbName = $"WbsktAuthRtMig_{Guid.NewGuid():N}";
        string connectionString = AuthSqlFixture.BuildDbConnectionString(dbName);

        // Shaped like AuthService mints them: 64 random bytes, base64.
        string retired = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        string live = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        try
        {
            await CreateDatabaseAsync(dbName);
            await ExecuteAsync(connectionString, PreMigrationSchema);
            await ExecuteAsync(connectionString,
                $"""
                INSERT INTO dbo.Users (Username, Email, PasswordHash) VALUES ('signed-in', 'in@example.test', 'hash');
                SET IDENTITY_INSERT dbo.RefreshTokens ON;
                INSERT INTO dbo.RefreshTokens (Id, UserId, Token, Expires, Revoked, ReplacedByToken) VALUES
                    (40, 1, '{retired}', DATEADD(day, 7, SYSUTCDATETIME()), SYSUTCDATETIME(), '{live}'),
                    (41, 1, '{live}',    DATEADD(day, 7, SYSUTCDATETIME()), NULL, NULL);
                SET IDENTITY_INSERT dbo.RefreshTokens OFF;
                """);

            await PublishAsync(dbName, connectionString);

            // Same rows, same Ids, and each hash is what SecurityTokens.Hash computes: SHA-256 of
            // the UTF-8 bytes of the token string.
            Assert.Equal(2, await ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM dbo.RefreshTokens"));
            Assert.Equal(41, await ScalarAsync<int>(connectionString,
                $"SELECT Id FROM dbo.RefreshTokens WHERE TokenHash = {Hex(Hash(live))} AND Revoked IS NULL"));
            Assert.Equal(40, await ScalarAsync<int>(connectionString,
                $"SELECT Id FROM dbo.RefreshTokens WHERE TokenHash = {Hex(Hash(retired))} AND ReplacedByTokenHash = {Hex(Hash(live))} AND Revoked IS NOT NULL"));

            // The plaintext is gone, and so is the scratch table that carried the rows.
            Assert.Equal(0, await ScalarAsync<int>(connectionString,
                "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RefreshTokens') AND name IN ('Token', 'ReplacedByToken')"));
            Assert.Equal(0, await ScalarAsync<int>(connectionString,
                "SELECT COUNT(*) FROM sys.tables WHERE name = '__RefreshTokenHashBackfill'"));

            // New sessions do not collide with the restored Ids.
            await ExecuteAsync(connectionString,
                $"INSERT INTO dbo.RefreshTokens (UserId, TokenHash, Expires) VALUES (1, {Hex(Hash("new"))}, SYSUTCDATETIME());");
            Assert.True(await ScalarAsync<int>(connectionString,
                $"SELECT Id FROM dbo.RefreshTokens WHERE TokenHash = {Hex(Hash("new"))}") > 41);

            // And the next deploy changes nothing.
            await PublishAsync(dbName, connectionString);
            Assert.Equal(3, await ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM dbo.RefreshTokens"));
        }
        finally
        {
            await AuthSqlFixture.DropDatabaseAsync(dbName);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Hex(byte[] bytes) => "0x" + Convert.ToHexString(bytes);

    private static Task PublishAsync(string dbName, string connectionString) =>
        DatabaseDeployer.DeployAsync(
            AuthSqlFixture.MasterConnectionString, dbName, connectionString,
            DatabaseDeployer.AuthProject, blockOnPossibleDataLoss: true);

    private static async Task CreateDatabaseAsync(string dbName)
    {
        await using var conn = new SqlConnection(AuthSqlFixture.MasterConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF DB_ID(N'{dbName}') IS NULL CREATE DATABASE [{dbName}];
            ALTER DATABASE [{dbName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
