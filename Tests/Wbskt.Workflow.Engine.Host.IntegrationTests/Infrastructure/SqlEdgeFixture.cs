using Microsoft.Data.SqlClient;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Per-collection xunit fixture that provisions a unique integration-test database,
/// deploys the workflow DACPAC, and tears the database down on dispose.
///
/// SQL Edge must be running and reachable. If it is not, <see cref="IsAvailable"/> is set to
/// <c>false</c> and every test on this fixture reports as <b>Skipped</b> — each one opens with
/// <c>Skip.IfNot(fixture.IsAvailable, …)</c> under a <c>[SkippableFact]</c>. Keep that shape for new
/// tests: a plain <c>[Fact]</c> that returns early instead would report as <i>passed</i> without
/// having run, which is how a whole suite can go green against no database at all.
///
/// Connection defaults:  Server=localhost,1433  User=sa  Password=Welcome1234
/// Override via env var: WBSKT_INTEGRATION_CONNSTR (full ADO.NET connection string to master DB).
/// </summary>
public sealed class SqlEdgeFixture : IAsyncLifetime
{
    private static readonly string MasterConnectionString =
        Environment.GetEnvironmentVariable("WBSKT_INTEGRATION_CONNSTR")
        ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Welcome1234;TrustServerCertificate=True;Connect Timeout=5;";

    private string? _dbName;

    public bool IsAvailable { get; private set; }

    /// <summary>ADO.NET connection string pointing at the unique per-fixture database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!await CanConnectToMasterAsync())
        {
            IsAvailable = false;
            return;
        }

        _dbName = $"WbsktIT_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}".Substring(0, 50);
        ConnectionString = BuildDbConnectionString(_dbName);

        try
        {
            await DatabaseDeployer.DeployAsync(MasterConnectionString, _dbName, ConnectionString);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Console.Error.WriteLine($"[SqlEdgeFixture] DACPAC deploy failed: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (!IsAvailable || _dbName is null)
        {
            return;
        }

        try
        {
            await using var conn = new SqlConnection(MasterConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                IF DB_ID(N'{_dbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_dbName}];
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SqlEdgeFixture] DROP DATABASE failed: {ex.Message}");
        }
    }

    private static async Task<bool> CanConnectToMasterAsync()
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

    private static string BuildDbConnectionString(string dbName)
    {
        var builder = new SqlConnectionStringBuilder(MasterConnectionString)
        {
            InitialCatalog = dbName,
            ConnectTimeout = 30
        };
        return builder.ConnectionString;
    }
}
