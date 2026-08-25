using Microsoft.Data.SqlClient;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// The <see cref="SqlEdgeFixture"/> equivalent for the <b>auth</b> database. Separate because the two
/// are separate databases in every environment — the auth host has its own connection string, its own
/// DACPAC and its own migration history, and a fixture that deployed both into one database would be
/// testing a shape that does not exist anywhere.
///
/// Same rules apply: SQL Server must be reachable, <see cref="IsAvailable"/> is false when it is not,
/// and every test opens with <c>Skip.IfNot(fixture.IsAvailable, …)</c> under a <c>[SkippableFact]</c>.
/// A plain <c>[Fact]</c> returning early would report as <i>passed</i> without having run.
///
/// Connection defaults:  Server=localhost,1433  User=sa  Password=Welcome1234
/// Override via env var: WBSKT_INTEGRATION_CONNSTR (full ADO.NET connection string to master DB).
/// </summary>
public sealed class AuthSqlFixture : IAsyncLifetime
{
    private string? _dbName;

    /// <summary>Connection string to <c>master</c>, for tests that need to make their own database.</summary>
    public static string MasterConnectionString =>
        Environment.GetEnvironmentVariable("WBSKT_INTEGRATION_CONNSTR")
        ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Welcome1234;TrustServerCertificate=True;Connect Timeout=5;";

    public bool IsAvailable { get; private set; }

    /// <summary>ADO.NET connection string pointing at the unique per-fixture auth database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!await CanConnectToMasterAsync())
        {
            IsAvailable = false;
            return;
        }

        _dbName = $"WbsktAuthIT_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..50];  // 58 chars before the slice
        ConnectionString = BuildDbConnectionString(_dbName);

        try
        {
            await DatabaseDeployer.DeployAsync(
                MasterConnectionString, _dbName, ConnectionString, DatabaseDeployer.AuthProject);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Console.Error.WriteLine($"[AuthSqlFixture] DACPAC deploy failed: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_dbName is null)
        {
            return;
        }

        await DropDatabaseAsync(_dbName);
    }

    public static string BuildDbConnectionString(string dbName) =>
        new SqlConnectionStringBuilder(MasterConnectionString) { InitialCatalog = dbName }.ConnectionString;

    public static async Task DropDatabaseAsync(string dbName)
    {
        try
        {
            await using var conn = new SqlConnection(MasterConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                IF DB_ID(N'{dbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{dbName}];
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AuthSqlFixture] DROP DATABASE failed: {ex.Message}");
        }
    }

    public static async Task<bool> CanConnectToMasterAsync()
    {
        try
        {
            await using var conn = new SqlConnection(MasterConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AuthSqlCollection : ICollectionFixture<AuthSqlFixture>
{
    public const string Name = "auth-sql";
}
