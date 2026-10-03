using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// The procedures behind client triggers with a hold time: finding the hold triggers a message
/// concerns, the holding / fired / clear state per trigger, and leasing holds whose time is up.
/// </summary>
[Collection("SqlEdge")]
public sealed class ClientHoldStateIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task Registrations_are_found_by_exact_type_or_wildcard_in_the_clients_workspace_only()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        Guid clientRef = Guid.NewGuid();
        string typed = await RegisterAsync(workspaceId, clientRef, "temp_c", 600);
        string wildcard = await RegisterAsync(workspaceId, clientRef, "*", 60);
        // '_' is a LIKE wildcard; a type that differs only there must not match.
        await RegisterAsync(workspaceId, clientRef, "tempXc", 600);
        // Another workspace naming this client's id must not see its messages.
        await RegisterAsync(workspaceId - 1, clientRef, "temp_c", 600);

        IReadOnlyCollection<TriggerRegistrationRow> rows = await Provider().GetRegistrationsAsync(clientRef, "temp_c", workspaceId, CancellationToken.None);

        Assert.Equal(new[] { typed, wildcard }.Order(), rows.Select(r => r.TriggerKey).Order());
        Assert.All(rows, r => Assert.Equal("{\"kind\":\"literal\",\"value\":true}", r.FilterExpression));
    }

    [SkippableFact]
    public async Task A_hold_fires_once_and_rearms_only_after_a_message_stops_matching()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var provider = Provider();
        string key = ClientHoldTriggerKey.Build(Guid.NewGuid(), "temperature", 60, Guid.NewGuid(), Guid.NewGuid());
        DateTime t0 = DateTime.UtcNow.AddMinutes(-10);

        // Matching since t0, and still matching a minute later: one hold, from t0, with the latest payload.
        await provider.RecordAsync(key, true, t0, 60, "{\"n\":1}", CancellationToken.None);
        await provider.RecordAsync(key, true, t0.AddMinutes(1), 60, "{\"n\":2}", CancellationToken.None);

        ClientHoldStateRow hold = (await provider.LeaseDueAsync(15, 1000, CancellationToken.None)).Single(h => h.TriggerKey == key);
        Assert.Equal(t0, hold.SinceAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(t0.AddSeconds(60), hold.DueAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal("{\"n\":2}", hold.Payload);

        await provider.MarkFiredAsync(hold.Id, hold.SinceAt, CancellationToken.None);
        Assert.Equal("fired", await StateAsync(key));

        // Still matching after firing: stays quiet.
        await provider.RecordAsync(key, true, t0.AddMinutes(2), 60, "{\"n\":3}", CancellationToken.None);
        Assert.Equal("fired", await StateAsync(key));
        Assert.DoesNotContain(await provider.LeaseDueAsync(15, 1000, CancellationToken.None), h => h.TriggerKey == key);

        // A non-matching message that arrives late (older than what was seen) changes nothing.
        await provider.RecordAsync(key, false, t0.AddSeconds(30), 60, "{}", CancellationToken.None);
        Assert.Equal("fired", await StateAsync(key));

        // A newer non-matching message clears it, and the next match starts a fresh hold.
        await provider.RecordAsync(key, false, t0.AddMinutes(3), 60, "{}", CancellationToken.None);
        Assert.Equal("clear", await StateAsync(key));
        await provider.RecordAsync(key, true, t0.AddMinutes(4), 60, "{\"n\":5}", CancellationToken.None);

        ClientHoldStateRow second = (await provider.LeaseDueAsync(15, 1000, CancellationToken.None)).Single(h => h.TriggerKey == key);
        Assert.Equal(t0.AddMinutes(4), second.SinceAt, TimeSpan.FromMilliseconds(1));

        await provider.DeleteByIdAsync(second.Id, CancellationToken.None);
    }

    [SkippableFact]
    public async Task Marking_an_old_hold_fired_leaves_a_newer_hold_alone()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var provider = Provider();
        string key = ClientHoldTriggerKey.Build(Guid.NewGuid(), "door", 0, Guid.NewGuid(), Guid.NewGuid());
        DateTime t0 = DateTime.UtcNow.AddMinutes(-10);
        await provider.RecordAsync(key, true, t0, 0, "{}", CancellationToken.None);
        ClientHoldStateRow first = (await provider.LeaseDueAsync(15, 1000, CancellationToken.None)).Single(h => h.TriggerKey == key);

        // Cleared and held again while the first hold was being dispatched.
        await provider.RecordAsync(key, false, t0.AddSeconds(1), 0, "{}", CancellationToken.None);
        await provider.RecordAsync(key, true, t0.AddSeconds(2), 0, "{}", CancellationToken.None);
        await provider.MarkFiredAsync(first.Id, first.SinceAt, CancellationToken.None);

        Assert.Equal("holding", await StateAsync(key));
        await provider.DeleteByIdAsync(first.Id, CancellationToken.None);
    }

    private ClientHoldStateProvider Provider() => new(ProviderFactory.BuildConfiguration(fixture.ConnectionString));

    private async Task<string> StateAsync(string key)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("SELECT State FROM dbo.ClientHoldStates WHERE TriggerKey = @p0", conn);
        cmd.Parameters.AddWithValue("@p0", key);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> RegisterAsync(int workspaceId, Guid clientRef, string type, int holdSeconds)
    {
        var workflowRefId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await ProcAsync("dbo.WorkflowDefinition_Publish",
            ("@RefId", workflowRefId), ("@WorkspaceId", workspaceId), ("@Name", "wf"), ("@Description", ""),
            ("@IsEnabled", true), ("@DefinitionJson", "{}"), ("@PublishedBy", 1));
        int definitionId;
        await using (var conn = await OpenAsync())
        await using (var cmd = new SqlCommand("SELECT Id FROM dbo.WorkflowDefinitions WHERE RefId = @p0", conn))
        {
            cmd.Parameters.AddWithValue("@p0", workflowRefId);
            definitionId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        string key = ClientHoldTriggerKey.Build(clientRef, type, holdSeconds, workflowRefId, nodeId);
        await ProcAsync("dbo.TriggerRegistration_Insert",
            ("@WorkflowDefinitionId", definitionId), ("@WorkflowRefId", workflowRefId), ("@WorkflowVersion", 1),
            ("@TriggerNodeId", nodeId), ("@TriggerKind", ClientHoldTriggerKey.TriggerKind), ("@TriggerKey", key),
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
