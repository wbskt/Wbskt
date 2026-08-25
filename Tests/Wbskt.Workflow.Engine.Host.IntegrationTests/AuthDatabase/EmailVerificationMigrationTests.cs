using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The <c>Users.IsEmailVerified</c> migration, run against a database shaped the way a real one was
/// before it.
///
/// This is the highest-consequence untested thing in the change that introduced it. Sign-in now
/// requires a confirmed address, and every account that predates the column was created when
/// registering proved nothing about the address. If the backfill does not run, the deploy locks every
/// existing user out of an account they have been using — and it is not the kind of failure that
/// shows up in a smoke test, because a fresh database looks perfect.
///
/// The script under test is the shipped one, read off disk rather than restated here, so a change to
/// it that forgets the backfill fails this test rather than agreeing with a copy.
/// </summary>
[Collection(AuthSqlCollection.Name)]
public sealed class EmailVerificationMigrationTests
{
    /// <summary>The shape of <c>dbo.Users</c> immediately before the column was added.</summary>
    private const string PreMigrationUsersTable =
        """
        CREATE TABLE dbo.Users (
            Id           INT              IDENTITY(1, 1) NOT NULL,
            RefId        UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
            Username     NVARCHAR(50)     NOT NULL,
            Email        NVARCHAR(100)    NOT NULL,
            PasswordHash NVARCHAR(255)    NOT NULL,
            IsActive     BIT              NOT NULL           DEFAULT 1,
            CreatedAt    DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_Users          PRIMARY KEY (Id),
            CONSTRAINT UQ_Users_RefId    UNIQUE (RefId),
            CONSTRAINT UQ_Users_Username UNIQUE (Username),
            CONSTRAINT UQ_Users_Email    UNIQUE (Email)
        );
        """;

    [SkippableFact]
    public async Task Accounts_that_predate_the_column_keep_their_access()
    {
        Skip.IfNot(await AuthSqlFixture.CanConnectToMasterAsync(), "SQL Server not reachable — skipping.");

        string dbName = $"WbsktAuthMig_{Guid.NewGuid():N}";
        string connectionString = AuthSqlFixture.BuildDbConnectionString(dbName);

        try
        {
            await CreateDatabaseAsync(dbName);
            await ExecuteAsync(connectionString, PreMigrationUsersTable);
            await ExecuteAsync(connectionString,
                """
                INSERT INTO dbo.Users (Username, Email, PasswordHash) VALUES
                    ('existing-one', 'one@example.test', 'hash'),
                    ('existing-two', 'two@example.test', 'hash');
                """);

            await RunPreDeploymentScriptAsync(connectionString);

            // Both survive the deploy able to sign in.
            Assert.Equal(2, await ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM dbo.Users WHERE IsEmailVerified = 1"));
            Assert.Equal(0, await ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM dbo.Users WHERE IsEmailVerified = 0"));
        }
        finally
        {
            await AuthSqlFixture.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The pre-deployment script runs on <b>every</b> deploy, not just the one that adds the column.
    /// If the guard were wrong, the second deploy would verify every account that had signed up since
    /// the first — silently turning the check off for exactly the accounts it exists for.
    /// </summary>
    [SkippableFact]
    public async Task Re_running_the_migration_does_not_verify_accounts_created_since()
    {
        Skip.IfNot(await AuthSqlFixture.CanConnectToMasterAsync(), "SQL Server not reachable — skipping.");

        string dbName = $"WbsktAuthMig_{Guid.NewGuid():N}";
        string connectionString = AuthSqlFixture.BuildDbConnectionString(dbName);

        try
        {
            await CreateDatabaseAsync(dbName);
            await ExecuteAsync(connectionString, PreMigrationUsersTable);
            await ExecuteAsync(connectionString,
                "INSERT INTO dbo.Users (Username, Email, PasswordHash) VALUES ('grandfathered', 'old@example.test', 'hash');");

            await RunPreDeploymentScriptAsync(connectionString);

            // Someone signs up after the migration. They are unverified, correctly.
            await ExecuteAsync(connectionString,
                "INSERT INTO dbo.Users (Username, Email, PasswordHash) VALUES ('newcomer', 'new@example.test', 'hash');");
            Assert.False(await ScalarAsync<bool>(connectionString, "SELECT IsEmailVerified FROM dbo.Users WHERE Username = 'newcomer'"));

            // The next deploy.
            await RunPreDeploymentScriptAsync(connectionString);

            Assert.True(await ScalarAsync<bool>(connectionString, "SELECT IsEmailVerified FROM dbo.Users WHERE Username = 'grandfathered'"));
            Assert.False(await ScalarAsync<bool>(connectionString, "SELECT IsEmailVerified FROM dbo.Users WHERE Username = 'newcomer'"));
        }
        finally
        {
            await AuthSqlFixture.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The script also runs against a database that does not exist yet, before any table has been
    /// created. Guarding only on the column would make it throw there and fail every fresh deploy.
    /// </summary>
    [SkippableFact]
    public async Task The_migration_is_a_no_op_on_a_database_with_no_Users_table()
    {
        Skip.IfNot(await AuthSqlFixture.CanConnectToMasterAsync(), "SQL Server not reachable — skipping.");

        string dbName = $"WbsktAuthMig_{Guid.NewGuid():N}";
        string connectionString = AuthSqlFixture.BuildDbConnectionString(dbName);

        try
        {
            await CreateDatabaseAsync(dbName);

            // No throw is the assertion.
            await RunPreDeploymentScriptAsync(connectionString);
        }
        finally
        {
            await AuthSqlFixture.DropDatabaseAsync(dbName);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Executes <c>Databases/Wbskt.Database.Auth/Scripts/Script.PreDeployment.sql</c> as sqlpackage
    /// would: split on <c>GO</c>, each batch its own command.
    /// </summary>
    private static async Task RunPreDeploymentScriptAsync(string connectionString)
    {
        string path = Path.Combine(SolutionRoot(), "Databases", "Wbskt.Database.Auth", "Scripts", "Script.PreDeployment.sql");
        string script = await File.ReadAllTextAsync(path);

        foreach (string batch in SplitBatches(script))
        {
            await ExecuteAsync(connectionString, batch);
        }
    }

    /// <summary>
    /// <c>GO</c> is a batch separator understood by the client, not a T-SQL statement, so SqlCommand
    /// rejects it. Split on lines that are nothing but <c>GO</c> — a naive Split on the two letters
    /// would cut any identifier containing them in half.
    /// </summary>
    internal static IReadOnlyList<string> SplitBatches(string script) =>
        Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(batch => batch.Trim())
            .Where(batch => batch.Length > 0)
            .ToArray();

    private static string SolutionRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Wbskt.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the solution root (Wbskt.slnx).");
    }

    /// <summary>
    /// Creates the database with READ_COMMITTED_SNAPSHOT already on, from a <c>master</c> connection.
    ///
    /// The pre-deployment script's first batch turns RCSI on with <c>ROLLBACK IMMEDIATE</c>, which is
    /// fine when sqlpackage runs it and a hazard when this test does: the batch would be executing on
    /// a connection to the very database it is kicking connections off. Setting it up front makes
    /// that batch's own <c>IF … = 0</c> guard skip it, which is exactly what happens on every
    /// already-migrated database. The batch under test is the one after it.
    /// </summary>
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
