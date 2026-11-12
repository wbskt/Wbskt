using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.E2E;

/// <summary>
/// wbskt/Wbskt#768: a wake is claimed by deleting its bookmark and then dispatched in memory. These run a
/// real workflow to a park, claim the wake as the scheduler or an inbound event would, stop before the
/// dispatch (the engine going down), and then recover as a new leader does: the branch must leave the
/// node exactly as the lost dispatch would have made it, with the wake the claim stored.
/// </summary>
[Collection("SqlEdge")]
public sealed class LostWakeRecoveryIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.";

    private static readonly Guid TriggerNodeId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WaitNodeId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DefaultEndId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TimeoutEndId = new("44444444-4444-4444-4444-444444444444");

    [SkippableFact]
    public async Task A_signal_wait_whose_ttl_claim_was_lost_leaves_by_its_timeout_port()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        await using ServiceProvider services = BuildServices();
        (long runId, long branchId) = await ParkAsync(services, AwaitSignal("""{ "signalName": "go", "ttl": "00:00:00.001" }"""));

        // The scheduler claims the expired TTL, then the engine stops before the dispatch.
        IReadOnlyCollection<BookmarkRow> claimed = await ClaimDueAsync(services);
        claimed.Should().Contain(bookmark => bookmark.RunId == runId);

        await RecoverAsync(services, runId, branchId);

        (await EndedAtAsync(services, runId)).Should().Be(TimeoutEndId);
    }

    [SkippableFact]
    public async Task A_signal_whose_dispatch_was_lost_resumes_with_its_payload()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        await using ServiceProvider services = BuildServices();
        (long runId, long branchId) = await ParkAsync(services, AwaitSignal("""{ "signalName": "go" }"""));

        // The signal arrives and claims the bookmark; the dispatch it then makes is recorded, not run.
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            BookmarkRow bookmark = (await scope.ServiceProvider.GetRequiredService<IBookmarkProvider>().GetAllByRunIdAsync((int)runId, CancellationToken.None)).Single();
            var signal = new InboundEvent(
                ChannelKind: "signal",
                MatchKeys: [bookmark.MatchKey],
                InboundEventId: Guid.NewGuid().ToString(),
                Payload: new Dictionary<string, JsonElement> { ["answer"] = JsonSerializer.SerializeToElement(42) },
                ReceivedAt: DateTime.UtcNow);
            BookmarkMatchResult match = await scope.ServiceProvider.GetRequiredService<IBookmarkResumer>().MatchInboundAsync(signal, false, CancellationToken.None);
            match.Matched.Should().BeTrue();
        }

        await RecoverAsync(services, runId, branchId);

        (await EndedAtAsync(services, runId)).Should().Be(DefaultEndId);
        BranchRow branch = await services.GetRequiredService<IBranchProvider>().GetByIdAsync(branchId, CancellationToken.None);
        branch.LocalJson.Should().Contain("\"answer\":42");
    }

    [SkippableFact]
    public async Task A_delay_whose_timer_claim_was_lost_moves_on()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        await using ServiceProvider services = BuildServices();
        (long runId, long branchId) = await ParkAsync(services, Node(WaitNodeId, "control:delay", """{ "duration": "00:00:00.001" }"""));

        (await ClaimDueAsync(services)).Should().Contain(bookmark => bookmark.RunId == runId);

        await RecoverAsync(services, runId, branchId);

        (await EndedAtAsync(services, runId)).Should().Be(DefaultEndId);
    }

    [SkippableFact]
    public async Task A_branch_left_waiting_without_a_claim_is_not_resumed()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        await using ServiceProvider services = BuildServices();
        (long runId, long branchId) = await ParkAsync(services, AwaitSignal("""{ "signalName": "go" }"""));

        // The park's bookmark is gone without a claim, as when a stop fell between the Waiting write and
        // the bookmark insert: nothing says the signal came, so recovery must not run the node on.
        await services.GetRequiredService<IBookmarkProvider>().DeleteAllByRunIdAsync((int)runId, CancellationToken.None);

        IReadOnlyCollection<BranchExecutionRequest> woken = await Recovery(services).FindWokenAsync(CancellationToken.None);

        woken.Should().NotContain(request => request.BranchId == branchId);
    }

    private ServiceProvider BuildServices()
    {
        ServiceCollection services = EngineTestHost.BuildServices(fixture.ConnectionString);

        // Nothing runs a dispatch on its own: a dispatch that is only recorded is one lost with the engine.
        services.AddSingleton<IRunDispatcher, RecordingRunDispatcher>();
        return services.BuildServiceProvider();
    }

    private static RunRecoveryService Recovery(IServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<IRunDispatcher>(),
        NullLogger<RunRecoveryService>.Instance);

    private static async Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(IServiceProvider services)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        return await services.GetRequiredService<IBookmarkProvider>().ClaimDueAsync(DateTime.UtcNow.AddMinutes(1), 1000, CancellationToken.None);
    }

    /// <summary>A new leader's recovery: find the woken branches, then dispatch them, and run the one under test.</summary>
    private static async Task RecoverAsync(IServiceProvider services, long runId, long branchId)
    {
        RunRecoveryService recovery = Recovery(services);
        IReadOnlyCollection<BranchExecutionRequest> woken = await recovery.FindWokenAsync(CancellationToken.None);
        woken.Should().Contain(new BranchExecutionRequest(runId, branchId, BranchExecutionReason.Recovered));

        var dispatcher = (RecordingRunDispatcher)services.GetRequiredService<IRunDispatcher>();
        dispatcher.DispatchedBranches.Clear();
        await recovery.RecoverAsync(woken, CancellationToken.None);
        dispatcher.DispatchedBranches.Should().ContainSingle(request => request.BranchId == branchId);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBranchLoop>().RunAsync(runId, branchId, BranchExecutionReason.Recovered, CancellationToken.None);
    }

    private static async Task<(long RunId, long BranchId)> ParkAsync(IServiceProvider services, string waitNode)
    {
        Guid workflowRefId = Guid.NewGuid();
        string definitionJson = $$"""
            {
              "workflowRefId": "{{workflowRefId}}", "version": 1, "workspaceId": 1, "name": "IT-LostWake",
              "description": null, "isEnabled": true,
              "nodes": [
                {{Node(TriggerNodeId, "trigger:manual", "{}")}},
                {{waitNode}},
                {{Node(DefaultEndId, "control:end", "{}")}},
                {{Node(TimeoutEndId, "control:end", "{}")}}
              ],
              "edges": [
                { "from": ["{{TriggerNodeId}}", "default"], "to": ["{{WaitNodeId}}", "in"] },
                { "from": ["{{WaitNodeId}}", "default"], "to": ["{{DefaultEndId}}", "in"] },
                { "from": ["{{WaitNodeId}}", "timeout"], "to": ["{{TimeoutEndId}}", "in"] }
              ],
              "sharedVariableSchema": [], "createdAt": "2026-01-01T00:00:00Z", "publishedBy": 1,
              "failFast": false, "runCompensationOnFailure": false
            }
            """;

        WorkflowDefinitionRow wd = await services.GetRequiredService<IWorkflowDefinitionProvider>().InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = workflowRefId, Version = 0, WorkspaceId = 1, Name = $"IT-LostWake-{workflowRefId:N}",
            Description = null, IsEnabled = true, DefinitionJson = definitionJson, PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        (long runId, _, long branchId, _) = await scope.ServiceProvider.GetRequiredService<IRunStarter>().StartAsync(
            wd.Id,
            TriggerNodeId.ToString(),
            new InboundEvent("manual", [$"manual:{wd.RefId}"], Guid.NewGuid().ToString(), new Dictionary<string, JsonElement> { ["workflowDefinitionRefId"] = JsonSerializer.SerializeToElement(wd.RefId) }, DateTime.UtcNow),
            null,
            CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBranchLoop>().RunAsync(runId, branchId, BranchExecutionReason.TriggerStarted, CancellationToken.None);

        BranchRow parked = await scope.ServiceProvider.GetRequiredService<IBranchProvider>().GetByIdAsync(branchId, CancellationToken.None);
        parked.Status.Should().Be(BranchStatus.Waiting);
        parked.NodeId.Should().Be(WaitNodeId);
        return (runId, branchId);
    }

    private static async Task<Guid?> EndedAtAsync(IServiceProvider services, long runId)
    {
        RunRow run = await services.GetRequiredService<IRunProvider>().GetByIdAsync(runId, CancellationToken.None);
        run.Status.Should().Be("Succeeded");

        IReadOnlyCollection<HistoryEventRow> events = await services.GetRequiredService<IHistoryEventProvider>().GetByRunIdAsync((int)runId, 0, 1000, CancellationToken.None);
        IReadOnlyCollection<Guid?> ends = events.Select(e => e.NodeId).Where(nodeId => nodeId == DefaultEndId || nodeId == TimeoutEndId).Distinct().ToArray();
        return ends.Should().ContainSingle(string.Join(", ", events.Select(e => $"{e.EventKind}@{e.NodeId}"))).Subject;
    }

    private static string AwaitSignal(string config) => Node(WaitNodeId, "control:awaitSignal", config);

    private static string Node(Guid nodeId, string kind, string config) => $$"""
        { "nodeId": "{{nodeId}}", "kind": "{{kind}}", "name": "{{kind}}", "ports": [], "config": {{config}} }
        """;
}
