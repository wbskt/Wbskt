using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Readings kept per plan (#632): dbo.ClientReadings_Compact rolls raw readings past the plan's raw days
/// into dbo.ClientReadingsHourly, deletes hours past its hourly days, and clears a deleted client's
/// history; dbo.ClientReadings_GetBuckets reads both tables without counting a reading twice.
/// </summary>
[Collection("SqlEdge")]
public sealed class ClientReadingRetentionIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";
    private const int BatchSize = 1_000_000;
    private static readonly DateTime Now = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddHours(12), DateTimeKind.Utc);

    private static readonly IReadOnlyCollection<ReadingRetention> Retention =
    [
        new(PlanCode.Free, 3, 30),
        new(PlanCode.Maker, 14, 365),
        new(PlanCode.Pro, 30, 730),
        new(PlanCode.Business, 30, 730)
    ];

    [SkippableFact]
    public async Task Readings_past_the_plans_raw_days_become_hours_and_a_chart_still_counts_every_one()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int client = await CreateClientAsync(plan: "Free");
        DateTime old = Now.AddDays(-5).AddMinutes(5);
        await ReadAsync(client, old, 10);
        await ReadAsync(client, old.AddMinutes(10), 20);
        await ReadAsync(client, old.AddMinutes(70), 40);
        await ReadAsync(client, Now.AddDays(-1), 7);
        ClientReadingBucket before = await DaysAsync(client);

        await CompactAsync();

        Assert.Equal(1, await CountAsync("dbo.ClientReadings", client));
        Assert.Equal(2, await CountAsync("dbo.ClientReadingsHourly", client));
        Assert.Equal(3, await ScalarAsync<int>("SELECT SUM(ReadingCount) FROM dbo.ClientReadingsHourly WHERE ClientId = @p0", client));
        Assert.Equal(before, await DaysAsync(client));
        Assert.Equal((4L, 7d, 40d, 77d / 4), (before.Count, before.Min, before.Max, before.Avg));
    }

    [SkippableFact]
    public async Task Each_plan_keeps_raw_readings_for_its_own_days_and_a_tenants_override_wins()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int free = await CreateClientAsync(plan: "Free");
        int pro = await CreateClientAsync(plan: "Pro");
        int overridden = await CreateClientAsync(plan: "Maker", overrides: """{"rawReadingsDays":1}""");
        foreach (int client in new[] { free, pro, overridden })
        {
            await ReadAsync(client, Now.AddDays(-4), 1);
        }

        await CompactAsync();

        Assert.Equal(0, await CountAsync("dbo.ClientReadings", free));
        Assert.Equal(1, await CountAsync("dbo.ClientReadings", pro));
        Assert.Equal(0, await CountAsync("dbo.ClientReadings", overridden));
    }

    [SkippableFact]
    public async Task Hours_past_the_plans_hourly_days_go_and_a_late_reading_adds_to_its_hour()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int client = await CreateClientAsync(plan: "Free");
        DateTime hour = Now.AddDays(-10).AddMinutes(1);
        await ReadAsync(client, Now.AddDays(-40), 5);
        await ReadAsync(client, hour, 2);
        await CompactAsync();

        await ReadAsync(client, hour.AddMinutes(30), 8, receivedAt: Now.AddDays(-4));
        await CompactAsync();

        Assert.Equal(0, await CountAsync("dbo.ClientReadings", client));
        Assert.Equal(1, await CountAsync("dbo.ClientReadingsHourly", client));
        Assert.Equal((2, 2d, 8d, 10d), await ScalarRowAsync(
            "SELECT ReadingCount, MinValue, MaxValue, SumValue FROM dbo.ClientReadingsHourly WHERE ClientId = @p0", client));
    }

    [SkippableFact]
    public async Task A_workspace_with_no_copy_of_its_account_loses_no_readings()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int client = await CreateClientAsync(plan: null);
        await ReadAsync(client, Now.AddDays(-40), 1);

        await CompactAsync();

        Assert.Equal(1, await CountAsync("dbo.ClientReadings", client));
    }

    [SkippableFact]
    public async Task A_downgraded_workspace_keeps_its_old_plans_readings_while_the_hold_runs()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int held = await CreateClientAsync(plan: "Free");
        int lapsed = await CreateClientAsync(plan: "Free");
        await ScalarAsync<int>("""
            UPDATE dbo.WorkspaceAccounts
            SET HeldRawReadingsDays = 30, HeldHourlyReadingsDays = 730, RetentionHeldUntil = DATEADD(DAY, 1, @p1)
            WHERE WorkspaceId = (SELECT WorkspaceId FROM dbo.Clients WHERE Id = @p0);
            UPDATE dbo.WorkspaceAccounts
            SET HeldRawReadingsDays = 30, HeldHourlyReadingsDays = 730, RetentionHeldUntil = DATEADD(DAY, -1, @p1)
            WHERE WorkspaceId = (SELECT WorkspaceId FROM dbo.Clients WHERE Id = @p2);
            SELECT 0;
            """, held, Now, lapsed);
        foreach (int client in new[] { held, lapsed })
        {
            await ReadAsync(client, Now.AddDays(-10), 1);
            await ReadAsync(client, Now.AddDays(-100), 2);
        }

        await CompactAsync();

        const string OldHours = "SELECT COUNT(*) FROM dbo.ClientReadingsHourly WHERE ClientId = @p0 AND HourStart < @p1";
        Assert.Equal(1, await CountAsync("dbo.ClientReadings", held));
        Assert.Equal(1, await ScalarAsync<int>(OldHours, held, Now.AddDays(-50)));
        Assert.Equal(0, await CountAsync("dbo.ClientReadings", lapsed));
        Assert.Equal(0, await ScalarAsync<int>(OldHours, lapsed, Now.AddDays(-50)));
    }

    [SkippableFact]
    public async Task A_deleted_clients_readings_are_cleared_later_and_nothing_lands_after()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue - 1);
        int client = await CreateClientAsync(plan: null, workspaceId);
        await ReadAsync(client, Now.AddDays(-5), 1);
        await CompactAsync();
        await ReadAsync(client, Now.AddHours(-1), 2);

        Assert.True(await new ClientProvider(Configuration()).DeleteAsync(client, workspaceId));
        await ReadAsync(client, Now, 3);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DeletedClientReadings WHERE ClientId = @p0", client));
        await CompactAsync();

        Assert.Equal(0, await CountAsync("dbo.ClientReadings", client));
        Assert.Equal(0, await CountAsync("dbo.ClientReadingsHourly", client));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DeletedClientReadings WHERE ClientId = @p0", client));
    }

    [SkippableFact]
    public async Task A_deleted_workspaces_readings_are_cleared_and_nothing_lands_after()
    {
        // #773: its clients are revoked, not deleted, and its account copy is kept for the meters, so
        // without the queue the plan's days would keep the readings for up to two years.
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue - 1);
        int client = await CreateClientAsync(plan: "Business", workspaceId);
        int neighbour = await CreateClientAsync(plan: "Business");
        await ReadAsync(client, Now.AddDays(-40), 1);
        await CompactAsync();
        await ReadAsync(client, Now.AddHours(-1), 2);
        await ReadAsync(neighbour, Now.AddHours(-1), 2);

        await RetireAsync(workspaceId);
        await ReadAsync(client, Now, 3);
        await CompactAsync();

        Assert.Equal(0, await CountAsync("dbo.ClientReadings", client));
        Assert.Equal(0, await CountAsync("dbo.ClientReadingsHourly", client));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DeletedClientReadings WHERE ClientId = @p0", client));
        Assert.Equal(1, await CountAsync("dbo.ClientReadings", neighbour));
    }

    private Task<ReadingCompaction> CompactAsync() => Provider().CompactAsync(Now, Retention, BatchSize);

    private Task ReadAsync(int clientId, DateTime sentAt, double value, DateTime? receivedAt = null) =>
        Provider().InsertAsync(clientId, sentAt, receivedAt ?? sentAt, isLate: receivedAt is not null, [new ClientReadingValue("temp", value)]);

    private async Task<ClientReadingBucket> DaysAsync(int clientId)
    {
        DateTime from = Now.AddDays(-7).Date;
        var buckets = await Provider().GetBucketsAsync(clientId, "temp", from, Now.AddDays(1), 8 * 86400);
        return Assert.Single(buckets);
    }

    private ClientReadingProvider Provider() => new(Configuration());

    private IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();

    private async Task<int> CreateClientAsync(string? plan, int? workspace = null, string? overrides = null)
    {
        int workspaceId = workspace ?? Random.Shared.Next(1_000_000, int.MaxValue - 1);
        if (plan is not null)
        {
            await ScalarAsync<int>("""
                INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, LimitOverrides, PlanRevision)
                VALUES (@p0, @p0, NEWID(), @p1, @p2, 1);
                SELECT 0;
                """, workspaceId, plan, (object?)overrides ?? DBNull.Value);
        }

        int policyId = await ScalarAsync<int>("""
            INSERT INTO dbo.RegistrationPolicies (RefId, WorkspaceId, Pin, Name)
            OUTPUT INSERTED.Id VALUES (NEWID(), @p0, @p1, 'readings-retention');
            """, workspaceId, Guid.NewGuid().ToString("N")[..20]);
        return await ScalarAsync<int>("""
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash, Status)
            OUTPUT INSERTED.Id VALUES (@p0, @p1, 'readings-retention', 0x00, 1);
            """, workspaceId, policyId);
    }

    private async Task RetireAsync(int workspaceId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_Retire", conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        await cmd.ExecuteNonQueryAsync();
    }

    private Task<int> CountAsync(string table, int clientId) =>
        ScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE ClientId = @p0", clientId);

    private async Task<(int, double, double, double)> ScalarRowAsync(string sql, int clientId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@p0", clientId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3));
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
