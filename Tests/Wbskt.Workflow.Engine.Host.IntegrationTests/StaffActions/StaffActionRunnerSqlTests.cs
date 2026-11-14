using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.StaffActions;

/// <summary>
/// The staff action runner against a real database: the change, made on a connection of its own as a
/// provider makes it, commits or rolls back with its staff audit row without the transaction becoming a
/// distributed one, and two calls with one idempotency key run the change once.
/// </summary>
[Collection("SqlEdge")]
public sealed class StaffActionRunnerSqlTests(SqlEdgeFixture main)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    private StaffActionRunner Runner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Main"] = main.ConnectionString })
            .Build();
        return new StaffActionRunner(new StaffAuditProvider(configuration, "Main"), NullLogger<StaffActionRunner>.Instance);
    }

    private static StaffChange Staff(Guid key) => new(Guid.NewGuid(), "pika", "Integration test of the staff runner", key);

    private static StaffAction Probe(Guid target) => new("ProbeChange", "Account", target);

    [SkippableFact]
    public async Task A_change_on_its_own_connection_commits_with_its_row()
    {
        Skip.IfNot(main.IsAvailable, Skipped);
        await EnsureProbeTableAsync();
        var target = Guid.NewGuid();
        var key = Guid.NewGuid();

        var result = await Runner().RunAsync(Staff(key), Probe(target), ct => WriteProbeAsync(target, Result.Success(), ct), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.StaffActionProbe WHERE Target = @Id;", target));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @Id AND Outcome = N'Succeeded';", key));
    }

    [SkippableFact]
    public async Task A_refused_change_leaves_only_its_refused_row()
    {
        Skip.IfNot(main.IsAvailable, Skipped);
        await EnsureProbeTableAsync();
        var target = Guid.NewGuid();
        var key = Guid.NewGuid();

        var result = await Runner().RunAsync(
            Staff(key),
            Probe(target),
            ct => WriteProbeAsync(target, Result.Failure(Error.Conflict("PROBE_REFUSED", "no")), ct),
            CancellationToken.None);

        Assert.Equal("PROBE_REFUSED", result.Error.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.StaffActionProbe WHERE Target = @Id;", target));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @Id AND Outcome = N'Refused' AND Detail = N'PROBE_REFUSED';", key));
    }

    [SkippableFact]
    public async Task Two_calls_with_one_key_at_once_make_the_change_once()
    {
        Skip.IfNot(main.IsAvailable, Skipped);
        await EnsureProbeTableAsync();
        var target = Guid.NewGuid();
        var key = Guid.NewGuid();
        var gate = new TaskCompletionSource();

        // The first call holds its transaction open until the second has had time to reach the key.
        var first = Runner().RunAsync(Staff(key), Probe(target), async ct =>
        {
            var written = await WriteProbeAsync(target, Result.Success(), ct);
            await gate.Task;
            return written;
        }, CancellationToken.None);
        await Task.Delay(200);
        var second = Runner().RunAsync(Staff(key), Probe(target), ct => WriteProbeAsync(target, Result.Success(), ct), CancellationToken.None);
        await Task.Delay(300);
        gate.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal([false, true], results.Select(r => r.Value.Replayed).Order());
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.StaffActionProbe WHERE Target = @Id;", target));
    }

    private async Task<Result> WriteProbeAsync(Guid target, Result outcome, CancellationToken ct)
    {
        await using var conn = new SqlConnection(main.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("INSERT INTO dbo.StaffActionProbe (Target) VALUES (@Target);", conn);
        cmd.Parameters.AddWithValue("@Target", target);
        await cmd.ExecuteNonQueryAsync(ct);
        return outcome;
    }

    private async Task EnsureProbeTableAsync()
    {
        await using var conn = new SqlConnection(main.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "IF OBJECT_ID(N'dbo.StaffActionProbe') IS NULL CREATE TABLE dbo.StaffActionProbe (Target UNIQUEIDENTIFIER NOT NULL);", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string sql, Guid id)
    {
        await using var conn = new SqlConnection(main.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
