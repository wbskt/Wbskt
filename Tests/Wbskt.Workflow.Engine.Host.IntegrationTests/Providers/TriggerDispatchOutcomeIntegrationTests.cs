using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;
using Wbskt.Workflow.Engine.Host.Providers;
using Wbskt.Workflow.Providers;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>Each trigger's recent outcomes (#416): a bounded ring per trigger node, and its hold state.</summary>
[Collection("SqlEdge")]
public sealed class TriggerDispatchOutcomeIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task Only_the_newest_outcomes_of_each_trigger_are_kept_newest_first()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var provider = Provider();
        Guid workflowRef = Guid.NewGuid();
        Guid node = Guid.NewGuid();
        Guid otherNode = Guid.NewGuid();
        DateTime t0 = new(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < ITriggerDispatchOutcomeProvider.Keep + 5; i++)
        {
            await provider.RecordAsync(Row(workflowRef, node, t0.AddSeconds(i), $"type-{i}"), CancellationToken.None);
        }
        await provider.RecordAsync(Row(workflowRef, otherNode, t0, "other"), CancellationToken.None);

        Assert.Equal(ITriggerDispatchOutcomeProvider.Keep, await CountAsync(workflowRef, node));
        var recent = (await provider.GetRecentAsync(workflowRef, node, 100, CancellationToken.None)).ToList();
        Assert.Equal(ITriggerDispatchOutcomeProvider.Keep, recent.Count);
        Assert.Equal($"type-{ITriggerDispatchOutcomeProvider.Keep + 4}", recent[0].MessageType);
        Assert.Equal("type-5", recent[^1].MessageType);
        Assert.Equal(t0.AddSeconds(ITriggerDispatchOutcomeProvider.Keep + 4), recent[0].OccurredAt);
        // Another trigger's ring is its own.
        Assert.Equal("other", Assert.Single(await provider.GetRecentAsync(workflowRef, otherNode, 100, CancellationToken.None)).MessageType);
    }

    [SkippableFact]
    public async Task A_hold_triggers_state_is_found_by_its_workflow_and_node()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        Guid workflowRef = Guid.NewGuid();
        Guid node = Guid.NewGuid();
        string key = await RegisterHoldAsync(workflowRef, node);
        DateTime t0 = DateTime.UtcNow.AddMinutes(-1);

        Assert.Null(await Provider().FindHoldStateAsync(workflowRef, node, CancellationToken.None));

        await new ClientHoldStateProvider(ProviderFactory.BuildConfiguration(fixture.ConnectionString)).RecordAsync(key, true, t0, 600, "{}", CancellationToken.None);
        TriggerHoldStateRow? hold = await Provider().FindHoldStateAsync(workflowRef, node, CancellationToken.None);

        Assert.NotNull(hold);
        Assert.Equal("holding", hold!.State);
        Assert.Equal(t0, hold.SinceAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(t0.AddSeconds(600), hold.DueAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Null(await Provider().FindHoldStateAsync(workflowRef, Guid.NewGuid(), CancellationToken.None));
    }

    [SkippableFact]
    public async Task Deleting_a_workflow_drops_its_triggers_outcomes()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        Guid workflowRef = Guid.NewGuid();
        Guid node = Guid.NewGuid();
        await PublishAsync(workflowRef, workspaceId);
        await Provider().RecordAsync(Row(workflowRef, node, DateTime.UtcNow, "t"), CancellationToken.None);

        await ProcAsync("dbo.WorkflowDefinition_Delete", ("@RefId", workflowRef), ("@WorkspaceId", workspaceId), ("@DeletedBy", 1));

        Assert.Equal(0, await CountAsync(workflowRef, node));
    }

    private TriggerDispatchOutcomeProvider Provider() => new(ProviderFactory.BuildConfiguration(fixture.ConnectionString));

    private static TriggerDispatchOutcomeRow Row(Guid workflowRef, Guid node, DateTime at, string messageType) => new()
    {
        WorkflowRefId = workflowRef,
        TriggerNodeId = node,
        OccurredAt = at,
        ChannelKind = "client",
        MessageType = messageType,
        Outcome = "Filtered",
        CorrelationKey = "client:x:" + messageType,
        RunRefId = null
    };

    private async Task<int> CountAsync(Guid workflowRef, Guid node)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.TriggerDispatchOutcomes WHERE WorkflowRefId = @p0 AND TriggerNodeId = @p1", conn);
        cmd.Parameters.AddWithValue("@p0", workflowRef);
        cmd.Parameters.AddWithValue("@p1", node);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> PublishAsync(Guid workflowRef, int workspaceId)
    {
        await ProcAsync("dbo.WorkflowDefinition_Publish",
            ("@RefId", workflowRef), ("@WorkspaceId", workspaceId), ("@Name", "wf"), ("@Description", ""),
            ("@IsEnabled", true), ("@DefinitionJson", "{}"), ("@PublishedBy", 1));
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("SELECT Id FROM dbo.WorkflowDefinitions WHERE RefId = @p0", conn);
        cmd.Parameters.AddWithValue("@p0", workflowRef);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> RegisterHoldAsync(Guid workflowRef, Guid node)
    {
        int definitionId = await PublishAsync(workflowRef, Random.Shared.Next(1_000_000, int.MaxValue));
        string key = ClientHoldTriggerKey.Build(Guid.NewGuid(), "temperature", 600, workflowRef, node);
        await ProcAsync("dbo.TriggerRegistration_Insert",
            ("@WorkflowDefinitionId", definitionId), ("@WorkflowRefId", workflowRef), ("@WorkflowVersion", 1),
            ("@TriggerNodeId", node), ("@TriggerKind", ClientHoldTriggerKey.TriggerKind), ("@TriggerKey", key),
            ("@CorrelationExpression", DBNull.Value), ("@ConcurrencyPolicy", "Queue"),
            ("@FilterExpression", "{\"kind\":\"literal\",\"value\":true}"));
        return key;
    }

    private async Task ProcAsync(string procedure, params (string Name, object Value)[] parameters)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.NextResultAsync())
        {
        }
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
