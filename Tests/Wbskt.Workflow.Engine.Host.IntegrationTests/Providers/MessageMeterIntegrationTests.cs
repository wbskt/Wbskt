using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// The client message meter (#629): dbo.ClientTraffic_InsertBatch counts the messages and delivered
/// commands it writes into dbo.WorkspaceMessageUsage, and the management host reads each account's
/// month from it and records which workspaces were told they reached the limit.
/// </summary>
[Collection("SqlEdge")]
public sealed class MessageMeterIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task The_meter_counts_exactly_the_messages_and_deliveries_the_traffic_table_holds()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int received = await EventIdAsync("ClientMessageReceivedEvent");
        int delivered = await EventIdAsync("ClientCommandDeliveredEvent");
        int latency = await EventIdAsync("ClientLatencyMeasuredEvent");
        var duplicate = Guid.NewGuid();
        DateTime today = DateTime.UtcNow;
        DateTime yesterday = today.AddDays(-1);
        EventLogEntry Entry(int eventId, DateTime at, Guid? messageId = null) => new(eventId, "{}", at, workspaceId, MessageId: messageId ?? Guid.NewGuid());

        await TrafficAsync(Entry(received, today, duplicate), Entry(received, today, duplicate), Entry(received, today), Entry(delivered, today), Entry(latency, today), Entry(received, yesterday));
        await TrafficAsync(Entry(received, today, duplicate), Entry(delivered, today));

        int inTable = await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ClientTraffic WHERE WorkspaceId = @p0 AND EventId IN (@p1, @p2)", workspaceId, received, delivered);
        long metered = await ScalarAsync<long>(
            "SELECT SUM(Inbound + Outbound) FROM dbo.WorkspaceMessageUsage WHERE WorkspaceId = @p0", workspaceId);
        Assert.Equal(5, inTable);
        Assert.Equal(inTable, metered);
        Assert.Equal(2L, await ScalarAsync<long>(
            "SELECT Inbound FROM dbo.WorkspaceMessageUsage WHERE WorkspaceId = @p0 AND Day = CAST(@p1 AS DATE)", workspaceId, today));
        Assert.Equal(2L, await ScalarAsync<long>(
            "SELECT Outbound FROM dbo.WorkspaceMessageUsage WHERE WorkspaceId = @p0 AND Day = CAST(@p1 AS DATE)", workspaceId, today));
    }

    [SkippableFact]
    public async Task An_account_is_pooled_across_its_tenants_workspaces_and_each_is_announced_once()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = Random.Shared.Next(1_000_000, int.MaxValue);
        int busy = Random.Shared.Next(1_000_000, int.MaxValue);
        int quiet = busy - 1;
        int alone = busy - 2;
        await ExecAsync("""
            INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, LimitOverrides, PlanRevision)
            VALUES (@p0, @p2, NEWID(), N'Maker', NULL, 1), (@p1, @p2, NEWID(), N'Maker', N'{"messagesPerMonth":5}', 1);
            """, busy, quiet, tenantId);
        int received = await EventIdAsync("ClientMessageReceivedEvent");
        DateTime now = DateTime.UtcNow;
        await TrafficAsync(
            new EventLogEntry(received, "{}", now, busy, MessageId: Guid.NewGuid()),
            new EventLogEntry(received, "{}", now, busy, MessageId: Guid.NewGuid()),
            new EventLogEntry(received, "{}", now, alone, MessageId: Guid.NewGuid()));
        var monthStart = new DateOnly(now.Year, now.Month, 1);
        var provider = new MessageAllowanceProvider(Configuration());

        IReadOnlyCollection<WorkspaceMessageAccount> accounts = await provider.GetAccountsAsync(monthStart, CancellationToken.None);

        Assert.Equal(new WorkspaceMessageAccount(busy, PlanCode.Maker, null, 2), accounts.Single(a => a.WorkspaceId == busy));
        Assert.Equal(new WorkspaceMessageAccount(quiet, PlanCode.Maker, new PlanLimitOverrides { MessagesPerMonth = 5 }, 2), accounts.Single(a => a.WorkspaceId == quiet));
        Assert.Equal(new WorkspaceMessageAccount(alone, PlanCode.Free, null, 1), accounts.Single(a => a.WorkspaceId == alone));
        Assert.Equal([busy, quiet], (await provider.RecordNoticeAsync([busy, quiet], monthStart, 100, CancellationToken.None)).Order().Reverse());
        Assert.Equal([alone], await provider.RecordNoticeAsync([busy, alone], monthStart, 100, CancellationToken.None));
        Assert.Equal([busy], await provider.RecordNoticeAsync([busy], monthStart, 80, CancellationToken.None));
    }

    private async Task TrafficAsync(params EventLogEntry[] entries)
    {
        var provider = new EventProvider(Configuration());
        await provider.InsertClientTrafficBatchAsync(EventLogTable.Build(entries));
    }

    private IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();

    private Task<int> EventIdAsync(string eventName) =>
        ScalarAsync<int>("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Events WHERE EventName = @p0)
                INSERT INTO dbo.Events (EventName, EventCriticality) VALUES (@p0, 1);
            SELECT Id FROM dbo.Events WHERE EventName = @p0;
            """, eventName);

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        await cmd.ExecuteNonQueryAsync();
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
