using Microsoft.Data.SqlClient;
using Wbskt.Management.Host.Services;
using Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// The <c>Clients.Secret</c> → <c>Clients.SecretHash</c> migration in the workflow database's
/// pre-deployment script, run against databases shaped the way real ones can be when it reaches them.
///
/// The schema diff that follows the script drops the plaintext column, so any row the script fails to
/// hash is a device that can never authenticate again, with nothing left to recover it from. The script
/// under test is the shipped one, read off disk.
/// </summary>
[Collection("SqlEdge")]
public sealed class ClientSecretMigrationTests
{
    /// <summary>The shape of <c>dbo.Clients</c> before secrets were hashed, trimmed to what the script touches.</summary>
    private const string PreMigrationClientsTable =
        """
        CREATE TABLE dbo.Clients (
            Id     INT              IDENTITY(1, 1) NOT NULL,
            RefId  UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
            Name   NVARCHAR(100)    NOT NULL,
            Secret NVARCHAR(255)    NOT NULL,
            CONSTRAINT PK_Clients PRIMARY KEY (Id)
        );
        """;

    [SkippableFact]
    public async Task Every_existing_secret_is_hashed()
    {
        Skip.IfNot(await AuthSqlFixture.CanConnectToMasterAsync(), "SQL Server not reachable — skipping.");

        string dbName = $"WbsktClientMig_{Guid.NewGuid():N}";
        string connectionString = AuthSqlFixture.BuildDbConnectionString(dbName);
        string[] secrets = [ClientSecrets.Generate(), ClientSecrets.Generate()];

        try
        {
            await CreateDatabaseAsync(dbName);
            await ExecuteAsync(connectionString, PreMigrationClientsTable);
            await InsertClientsAsync(connectionString, secrets);

            await RunPreDeploymentScriptAsync(connectionString);

            await AssertEveryRowHashesItsSecretAsync(connectionString, secrets);
        }
        finally
        {
            await AuthSqlFixture.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The script's steps are not atomic. A run that dies after adding <c>SecretHash</c> but before
    /// filling it leaves the column present and NULL; the re-run must still backfill, or the diff
    /// drops the plaintext for every row it skipped.
    /// </summary>
    [SkippableFact]
    public async Task A_rerun_after_a_partial_migration_still_hashes_every_secret()
    {
        Skip.IfNot(await AuthSqlFixture.CanConnectToMasterAsync(), "SQL Server not reachable — skipping.");

        string dbName = $"WbsktClientMig_{Guid.NewGuid():N}";
        string connectionString = AuthSqlFixture.BuildDbConnectionString(dbName);
        string[] secrets = [ClientSecrets.Generate(), ClientSecrets.Generate()];

        try
        {
            await CreateDatabaseAsync(dbName);
            await ExecuteAsync(connectionString, PreMigrationClientsTable);
            await InsertClientsAsync(connectionString, secrets);

            // Where the interrupted run stopped: the column exists, nothing is in it.
            await ExecuteAsync(connectionString, "ALTER TABLE dbo.Clients ADD SecretHash VARBINARY(32) NULL;");

            await RunPreDeploymentScriptAsync(connectionString);

            await AssertEveryRowHashesItsSecretAsync(connectionString, secrets);
        }
        finally
        {
            await AuthSqlFixture.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// <c>Client_Verify</c> reads the dropped <c>Secret</c> column. Its file is gone from the project,
    /// but migrate.sh does not drop objects missing from source, so only the script removes it.
    /// </summary>
    [SkippableFact]
    public async Task The_retired_Client_Verify_procedure_is_dropped()
    {
        Skip.IfNot(await AuthSqlFixture.CanConnectToMasterAsync(), "SQL Server not reachable — skipping.");

        string dbName = $"WbsktClientMig_{Guid.NewGuid():N}";
        string connectionString = AuthSqlFixture.BuildDbConnectionString(dbName);

        try
        {
            await CreateDatabaseAsync(dbName);
            await ExecuteAsync(connectionString, PreMigrationClientsTable);
            await ExecuteAsync(connectionString,
                """
                CREATE PROCEDURE dbo.Client_Verify @RefId UNIQUEIDENTIFIER, @Secret NVARCHAR(255)
                AS
                SELECT Id FROM dbo.Clients WHERE RefId = @RefId AND Secret = @Secret;
                """);

            await RunPreDeploymentScriptAsync(connectionString);

            Assert.True(await ScalarAsync<bool>(connectionString,
                "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.Client_Verify', N'P') IS NULL THEN 1 ELSE 0 END AS BIT)"));
        }
        finally
        {
            await AuthSqlFixture.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>A fresh database has no tables yet when the script runs; it must pass straight through.</summary>
    [SkippableFact]
    public async Task The_migration_is_a_no_op_on_an_empty_database()
    {
        Skip.IfNot(await AuthSqlFixture.CanConnectToMasterAsync(), "SQL Server not reachable — skipping.");

        string dbName = $"WbsktClientMig_{Guid.NewGuid():N}";
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

    private static async Task InsertClientsAsync(string connectionString, IEnumerable<string> secrets)
    {
        foreach (string secret in secrets)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("INSERT INTO dbo.Clients (Name, Secret) VALUES (N'device', @Secret);", conn);
            cmd.Parameters.AddWithValue("@Secret", secret);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Each row's hash must be the one login computes, and the column must be NOT NULL so the diff's
    /// table definition applies without a rebuild.
    /// </summary>
    private static async Task AssertEveryRowHashesItsSecretAsync(string connectionString, IReadOnlyCollection<string> secrets)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        await using (var cmd = new SqlCommand("SELECT Secret, SecretHash FROM dbo.Clients;", conn))
        await using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            int rows = 0;
            while (await reader.ReadAsync())
            {
                rows++;
                string secret = reader.GetString(0);
                Assert.False(reader.IsDBNull(1), $"SecretHash is NULL for secret '{secret}'.");
                Assert.Equal(ClientSecrets.Hash(secret), (byte[])reader[1]);
            }

            Assert.Equal(secrets.Count, rows);
        }

        await using var nullable = new SqlCommand(
            "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Clients') AND name = N'SecretHash';", conn);
        Assert.False((bool)(await nullable.ExecuteScalarAsync())!);
    }

    /// <summary>
    /// Executes <c>Databases/Wbskt.Database/Scripts/Script.PreDeployment.sql</c> as sqlpackage would:
    /// split on <c>GO</c>, each batch its own command.
    /// </summary>
    private static async Task RunPreDeploymentScriptAsync(string connectionString)
    {
        string path = Path.Combine(SolutionRoot(), "Databases", "Wbskt.Database", "Scripts", "Script.PreDeployment.sql");
        string script = await File.ReadAllTextAsync(path);

        foreach (string batch in EmailVerificationMigrationTests.SplitBatches(script))
        {
            await ExecuteAsync(connectionString, batch);
        }
    }

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
    /// RCSI is turned on up front so the script's first batch skips its <c>ROLLBACK IMMEDIATE</c>,
    /// for the reason given on <see cref="EmailVerificationMigrationTests"/>'s equivalent.
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
