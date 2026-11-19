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

    private static readonly Guid StaffRef = Guid.NewGuid();

    private static ProbeRequest Staff(Guid key) => new(new StaffChange(StaffRef, "pika", "Integration test of the staff runner", key));

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

    [SkippableFact]
    public async Task Only_rows_past_the_retention_can_be_deleted_and_none_can_be_changed()
    {
        Skip.IfNot(main.IsAvailable, Skipped);
        var oldKey = Guid.NewGuid();
        var newKey = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT INTO dbo.StaffAudit (StaffRefId, StaffName, Action, TargetKind, Reason, Outcome, IdempotencyKey, CreatedAt)
            VALUES (NEWID(), N'pika', N'ProbeChange', N'Account', N'Retention integration test', N'Succeeded', @Old, DATEADD(YEAR, -8, SYSUTCDATETIME())),
                   (NEWID(), N'pika', N'ProbeChange', N'Account', N'Retention integration test', N'Succeeded', @New, SYSUTCDATETIME());
            """, oldKey, newKey);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Main"] = main.ConnectionString })
            .Build();
        var audit = new StaffAuditProvider(configuration, "Main");

        var young = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM dbo.StaffAudit WHERE IdempotencyKey = @Old OR IdempotencyKey = @New;", oldKey, newKey));
        var change = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE dbo.StaffAudit SET Reason = N'changed afterwards' WHERE IdempotencyKey = @Old OR IdempotencyKey = @New;", oldKey, newKey));
        int deleted = 0;
        while (await audit.DeleteExpiredAsync(DateTime.UtcNow.AddYears(-StaffAuditRetention.Years), 1000, CancellationToken.None) is var batch and > 0)
        {
            deleted += batch;
        }

        Assert.Equal(50090, young.Number);
        Assert.Equal(50090, change.Number);
        Assert.True(deleted >= 1);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @Id;", oldKey));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @Id;", newKey));
    }

    private async Task ExecuteAsync(string sql, Guid old, Guid young)
    {
        await using var conn = new SqlConnection(main.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Old", old);
        cmd.Parameters.AddWithValue("@New", young);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed record ProbeRequest(StaffChange Staff) : IStaffRequest;

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
