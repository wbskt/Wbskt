using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using FluentAssertions;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;
using Wbskt.Workflow.Extensions;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.E2E;

public sealed class RecordingRunDispatcher : IRunDispatcher
{
    public List<BranchExecutionRequest> DispatchedBranches { get; } = new();

    public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
    {
        DispatchedBranches.Add(request);
        return ValueTask.CompletedTask;
    }
}

[Collection("SqlEdge")]
public sealed class CrashRecoveryIntegrationTests(SqlEdgeFixture fixture)
{
    [SkippableFact]
    public async Task StartupRecoveryService_RecoversOrphanedBranch_AndDispatchesIt()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        ServiceCollection services = EngineTestHost.BuildServices(fixture.ConnectionString);
        
        await using ServiceProvider sp = services.BuildServiceProvider();
        
        var definitionProvider = sp.GetRequiredService<IWorkflowDefinitionProvider>();
        var runProvider = sp.GetRequiredService<IRunProvider>();
        var branchProvider = sp.GetRequiredService<IBranchProvider>();
        
        // 1. Publish a dummy workflow
        var workflowRefId = Guid.NewGuid();
        string dummyJson = $$"""
        {
            "workflowRefId": "{{workflowRefId}}",
            "version": 1,
            "workspaceId": 1,
            "name": "CrashRecovery",
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
        
        WorkflowDefinitionRow wd = await definitionProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0,
            RefId = workflowRefId,
            Version = 1,
            WorkspaceId = 1,
            Name = "CrashRecovery",
            Description = null,
            IsEnabled = true,
            DefinitionJson = dummyJson,
            PublishedBy = 1,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
        
        // 2. Insert a Run and an Orphaned Active Branch manually
        Guid runRefId = Guid.NewGuid();
        Guid branchRefId = Guid.NewGuid();
        
        var run = new RunRow
        {
            Id = 0,
            RefId = runRefId,
            WorkflowDefinitionId = wd.Id,
            WorkflowRefId = wd.RefId,
            WorkflowVersion = wd.Version,
            Status = "Running",
            TriggerNodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CorrelationKey = null,
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 1000m,
            CreatedAt = DateTime.UtcNow
        };
        
        var createdRun = await runProvider.CreateAsync(run, CancellationToken.None);
        
        var branch = new BranchRow
        {
            Id = 0,
            RefId = branchRefId,
            RunId = createdRun.Id,
            ParentBranchId = null,
            ForkCohortId = null,
            NodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Status = "Active",
            PendingTakePort = null,
            LocalJson = "{}",
            LastOutputJson = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            RowVersion = Array.Empty<byte>()
        };
        
        var createdBranch = await branchProvider.CreateAsync(branch, CancellationToken.None);
        
        // 3. Run Recovery Service with a Mock Dispatcher
        var mockDispatcher = new RecordingRunDispatcher();
        
        var mockScopeFactory = new MockServiceScopeFactory(branchProvider, runProvider, null);
        var recoveryService = new RunRecoveryService(
            mockScopeFactory,
            mockDispatcher,
            NullLogger<RunRecoveryService>.Instance);
            
        await recoveryService.RecoverAsync(CancellationToken.None);
        
        // 4. Verify it was dispatched
        // Containment, not ContainSingle: recovery deliberately picks up every orphaned branch in
        // the database, and every test in the SqlEdge collection shares one database - so branches
        // this test did not create are legitimately dispatched in the same pass. What this test is
        // about is that its own orphaned branch was among them, with the right reason.
        mockDispatcher.DispatchedBranches.Should().Contain(dispatched =>
            dispatched.RunId == createdRun.Id
            && dispatched.BranchId == createdBranch.Id
            && dispatched.Reason == Wbskt.Workflow.Abstraction.Runtime.BranchExecutionReason.Recovered);
    }

    [SkippableFact]
    public async Task StartupRecovery_ResumesAWaitingBranchWhoseWakeWasClaimedButNeverRun()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var definitionProvider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        var branchProvider = ProviderFactory.Branch(fixture.ConnectionString);
        var bookmarkProvider = ProviderFactory.Bookmark(fixture.ConnectionString);

        WorkflowDefinitionRow wd = await definitionProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 1, WorkspaceId = 1,
            Name = $"IT-WD-WAKE-{Guid.NewGuid():N}", Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""", PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
        RunRow run = await runProvider.CreateAsync(new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id, WorkflowRefId = wd.RefId, WorkflowVersion = wd.Version,
            TriggerNodeId = Guid.NewGuid(), CorrelationKey = null, Status = "Running",
            StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null,
            CancellationReason = null, CreditBudget = 0m, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        (BranchRow woken, BookmarkRow claimed) = await ParkAsync(branchProvider, bookmarkProvider, run.Id);
        (BranchRow stillParked, _) = await ParkAsync(branchProvider, bookmarkProvider, run.Id);

        // The timer claims the first branch's bookmark by deleting it, then the engine stops before the
        // in-memory wake reaches the branch loop.
        (await bookmarkProvider.TryClaimAsync(claimed.RefId, CancellationToken.None)).Should().BeTrue();

        var dispatcher = new RecordingRunDispatcher();
        var recovery = new RunRecoveryService(new MockServiceScopeFactory(branchProvider, runProvider, null), dispatcher, NullLogger<RunRecoveryService>.Instance);
        await recovery.RecoverAsync(CancellationToken.None);

        dispatcher.DispatchedBranches.Should().Contain(request =>
            request.BranchId == woken.Id && request.RunId == run.Id && request.Reason == BranchExecutionReason.Recovered);
        dispatcher.DispatchedBranches.Should().NotContain(request => request.BranchId == stillParked.Id);
    }

    private static async Task<(BranchRow Branch, BookmarkRow Bookmark)> ParkAsync(IBranchProvider branchProvider, IBookmarkProvider bookmarkProvider, int runId)
    {
        BranchRow branch = await branchProvider.CreateAsync(new BranchRow
        {
            Id = 0, RefId = Guid.NewGuid(), RunId = runId, ParentBranchId = null, ForkCohortId = null,
            NodeId = Guid.NewGuid(), Status = "Waiting", PendingTakePort = null, LocalJson = "{}",
            LastOutputJson = null, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, RowVersion = []
        }, CancellationToken.None);
        BookmarkRow bookmark = await bookmarkProvider.CreateAsync(new BookmarkRow
        {
            Id = 0, RefId = Guid.NewGuid(), RunId = runId, BranchRefId = branch.RefId,
            NodeId = branch.NodeId, WakeConditionKind = "timer", MatchKey = string.Empty,
            WakeConditionJson = """{"kind":"timer","at":"2000-01-01T00:00:00Z"}""",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), TtlPort = null, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
        return (branch, bookmark);
    }
}

public sealed class MockServiceScopeFactory : IServiceScopeFactory
{
    private readonly IBranchProvider _branchProvider;
    private readonly IRunProvider? _runProvider;
    private readonly IRunCancellationService? _runCancellationService;

    public MockServiceScopeFactory(IBranchProvider branchProvider, IRunProvider? runProvider, IRunCancellationService? runCancellationService)
    {
        _branchProvider = branchProvider;
        _runProvider = runProvider;
        _runCancellationService = runCancellationService;
    }

    public IServiceScope CreateScope()
    {
        return new MockServiceScope(_branchProvider, _runProvider, _runCancellationService);
    }
}

public sealed class MockServiceScope : IServiceScope
{
    public IServiceProvider ServiceProvider { get; }

    public MockServiceScope(IBranchProvider branchProvider, IRunProvider? runProvider, IRunCancellationService? runCancellationService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(branchProvider);
        if (runProvider != null) services.AddSingleton(runProvider);
        if (runCancellationService != null) services.AddSingleton(runCancellationService);
        ServiceProvider = services.BuildServiceProvider();
    }

    public void Dispose() { }
}
