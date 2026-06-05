using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;
using Wbskt.Workflow.Extensions;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.E2E;

/// <summary>
/// Task 13.8 — Full happy-path E2E: publish workflow → start run → execute via real BranchLoop
/// → verify run reaches a terminal state (Succeeded).
///
/// Uses real SQL providers and an in-process BranchLoop. No RabbitMQ or hosted services required.
///
/// Workflow: single ManualTrigger node with no outgoing edges.
/// The trigger executes, returns Continue("default"), but there is no edge →
/// the branch completes → RunFinalizer → run status = "Succeeded".
/// </summary>
[Collection("SqlEdge")]
public sealed class HappyPathIntegrationTests(SqlEdgeFixture fixture, ITestOutputHelper output)
{
    private const string MinimalWorkflowJson = """
        {
          "workflowRefId": "00000000-0000-0000-0000-000000000000",
          "version": 1,
          "workspaceId": 1,
          "name": "IT-E2E-Smoke",
          "description": null,
          "isEnabled": true,
          "nodes": [
            {
              "nodeId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "kind": "trigger:manual",
              "name": "Start",
              "ports": [{"portId": "out", "direction": "Output", "label": "Out"}],
              "config": {}
            }
          ],
          "edges": [],
          "sharedVariableSchema": [],
          "createdAt": "2026-01-01T00:00:00Z",
          "publishedBy": 1,
          "failFast": false,
          "runCompensationOnFailure": false
        }
        """;

    private static readonly Guid TriggerNodeId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Publish_start_execute_run_reaches_Succeeded()
    {
        if (!fixture.IsAvailable)
        {
            output.WriteLine("SKIPPED: SQL Edge not available. Start SQL Edge with sa/Welcome1234 on port 1433.");
            return;
        }

        // ── 1. Build a scoped DI container wired to the integration DB ──
        var services = new ServiceCollection();
        IConfiguration config = ProviderFactory.BuildConfiguration(fixture.ConnectionString);
        services.AddSingleton(config);
        services.AddWorkflowEngine(config);
        services.AddLogging();

        await using ServiceProvider sp = services.BuildServiceProvider();

        // ── 2. Publish a minimal ManualTrigger workflow ──
        var wdProvider = sp.GetRequiredService<IWorkflowDefinitionProvider>();

        Guid workflowRefId = Guid.NewGuid();

        // Replace placeholder refId in the JSON with a real one
        string definitionJson = MinimalWorkflowJson
            .Replace("00000000-0000-0000-0000-000000000000", workflowRefId.ToString());

        WorkflowDefinitionRow wd = await wdProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0,
            RefId = workflowRefId,
            Version = 0,
            WorkspaceId = 1,
            Name = $"IT-E2E-{workflowRefId:N}",
            Description = null,
            IsEnabled = true,
            DefinitionJson = definitionJson,
            PublishedBy = 1,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        output.WriteLine($"Published workflow Id={wd.Id} RefId={wd.RefId} Version={wd.Version}");

        // ── 3. Start a run via RunStarter ──
        using AsyncServiceScope scope = sp.CreateAsyncScope();
        var runStarter = scope.ServiceProvider.GetRequiredService<IRunStarter>();

        var triggerEvent = new InboundEvent(
            ChannelKind: "manual",
            CorrelationKey: "e2e-smoke",
            InboundEventId: Guid.NewGuid().ToString(),
            Payload: new Dictionary<string, JsonElement>
            {
                ["test"] = JsonSerializer.SerializeToElement("e2e")
            },
            ReceivedAt: DateTime.UtcNow);

        (long runId, long branchId) = await runStarter.StartAsync(
            wd.Id,
            TriggerNodeId.ToString(),
            triggerEvent,
            CancellationToken.None);

        output.WriteLine($"Started run RunId={runId} BranchId={branchId}");

        // ── 4. Seed the RunCounters to 1 (initial branch) ──
        // RunStarter does not call IncrementActiveBranchesAsync; set it here so
        // CompleteBranchAsync can decrement to 0 and fire RunFinalizer.
        var countersProvider = scope.ServiceProvider.GetRequiredService<IRunCountersProvider>();
        await countersProvider.IncrementActiveBranchesAsync((int)runId, 1, CancellationToken.None);

        // ── 5. Execute the branch via BranchLoop ──
        var branchLoop = scope.ServiceProvider.GetRequiredService<IBranchLoop>();
        await branchLoop.RunAsync(runId, branchId, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        output.WriteLine("BranchLoop completed");

        // ── 6. Verify the run reached a terminal state ──
        var runProvider = scope.ServiceProvider.GetRequiredService<IRunProvider>();
        RunRow finalRun = await runProvider.GetByIdAsync(runId, CancellationToken.None);

        output.WriteLine($"Run final status: {finalRun.Status}");

        finalRun.Status.Should().BeOneOf("Succeeded", "Failed", "PartiallyFailed", "Cancelled",
            "run must have reached a terminal state");

        // For the happy path (no errors, single trigger node, no edges), expect Succeeded
        finalRun.Status.Should().Be("Succeeded");
        finalRun.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Publish_start_execute_history_events_written()
    {
        if (!fixture.IsAvailable)
        {
            output.WriteLine("SKIPPED: SQL Edge not available.");
            return;
        }

        var services = new ServiceCollection();
        IConfiguration config = ProviderFactory.BuildConfiguration(fixture.ConnectionString);
        services.AddSingleton(config);
        services.AddWorkflowEngine(config);
        services.AddLogging();

        await using ServiceProvider sp = services.BuildServiceProvider();

        Guid workflowRefId = Guid.NewGuid();
        string definitionJson = MinimalWorkflowJson
            .Replace("00000000-0000-0000-0000-000000000000", workflowRefId.ToString());

        var wdProvider = sp.GetRequiredService<IWorkflowDefinitionProvider>();
        WorkflowDefinitionRow wd = await wdProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = workflowRefId, Version = 0, WorkspaceId = 1,
            Name = $"IT-E2E-Hist-{workflowRefId:N}",
            Description = null, IsEnabled = true,
            DefinitionJson = definitionJson, PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        using AsyncServiceScope scope = sp.CreateAsyncScope();
        var runStarter = scope.ServiceProvider.GetRequiredService<IRunStarter>();

        (long runId, long branchId) = await runStarter.StartAsync(
            wd.Id,
            TriggerNodeId.ToString(),
            new InboundEvent(
                ChannelKind: "manual",
                CorrelationKey: "hist-test",
                InboundEventId: Guid.NewGuid().ToString(),
                Payload: new Dictionary<string, JsonElement>(),
                ReceivedAt: DateTime.UtcNow),
            CancellationToken.None);

        var countersProvider = scope.ServiceProvider.GetRequiredService<IRunCountersProvider>();
        await countersProvider.IncrementActiveBranchesAsync((int)runId, 1, CancellationToken.None);

        var branchLoop = scope.ServiceProvider.GetRequiredService<IBranchLoop>();
        await branchLoop.RunAsync(runId, branchId, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        var historyProvider = scope.ServiceProvider.GetRequiredService<IHistoryEventProvider>();

        // Read all events (page from event id 0, large page size)
        var events = await historyProvider.GetByRunIdAsync((int)runId, 0, 1000, CancellationToken.None);

        events.Should().NotBeEmpty("BranchLoop and RunFinalizer must write history events");

        var eventKinds = events.Select(e => e.EventKind).ToHashSet();
        eventKinds.Should().Contain("RunStarted");
        eventKinds.Should().Contain("BranchCompleted");
        eventKinds.Should().Contain("RunFinalized");
    }
}
