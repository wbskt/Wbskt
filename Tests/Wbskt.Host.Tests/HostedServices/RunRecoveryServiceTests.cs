using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.HostedServices;

public sealed class RunRecoveryServiceTests
{
    [Fact]
    public async Task Start_dispatches_all_running_branches()
    {
        // Arrange
        var branches = new[]
        {
            Running(71, 171),
            Running(72, 172)
        };
        var branchProvider = new RecordingBranchProvider(branches);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(branchProvider), runDispatcher, logger);

        // Act
        await service.RecoverAsync([], CancellationToken.None);

        // Assert
        Assert.Equal([(171L, 71L, BranchExecutionReason.Recovered), (172L, 72L, BranchExecutionReason.Recovered)], runDispatcher.Requests);
    }

    [Fact]
    public async Task The_overlap_pass_runs_only_the_Active_branches_an_old_leader_left_with_nothing_to_drive_them()
    {
        DateTime now = new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
        var branches = new List<RunningBranch> { Running(71, 171) };
        var dispatcher = new ChannelRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(new RecordingBranchProvider(branches)), dispatcher, logger, dispatcher, new FixedClock(now));

        service.StartRecordingDispatches();
        await service.RecoverAsync([], CancellationToken.None);

        // While this leader ran: the old one started a run (72), this one started one and dispatched it
        // (74), and this one has just written one it has yet to dispatch (73).
        branches.Add(Running(72, 172));
        branches.Add(Running(73, 173) with { UpdatedAt = now.AddSeconds(-2) });
        branches.Add(Running(74, 174));
        await dispatcher.DispatchAsync(new BranchExecutionRequest(174, 74, BranchExecutionReason.TriggerStarted), CancellationToken.None);

        await service.RecoverOverlapAsync(CancellationToken.None);

        var dispatched = new List<(long BranchId, BranchExecutionReason Reason)>();
        while (dispatcher.Reader.TryRead(out BranchExecutionRequest? request))
        {
            dispatched.Add((request.BranchId, request.Reason));
        }

        Assert.Equal([(71L, BranchExecutionReason.Recovered), (74L, BranchExecutionReason.TriggerStarted), (72L, BranchExecutionReason.Recovered)], dispatched);
        Assert.Contains(logger.Messages, message => message.Contains("Recovered 1 Active branches the previous leader left", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_overlap_pass_stops_recording()
    {
        var dispatcher = new ChannelRunDispatcher();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(new RecordingBranchProvider([])), dispatcher, new RecordingLogger<RunRecoveryService>(), dispatcher);
        service.StartRecordingDispatches();

        await service.RecoverOverlapAsync(CancellationToken.None);
        await dispatcher.DispatchAsync(new BranchExecutionRequest(1, 2, BranchExecutionReason.TriggerStarted), CancellationToken.None);

        Assert.Empty(dispatcher.StopRecording());
    }

    [Fact]
    public async Task Start_resumes_the_woken_branches_it_was_given_after_the_running_ones()
    {
        // Arrange
        var branchProvider = new RecordingBranchProvider([Running(71, 171)]);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(branchProvider), runDispatcher, logger);

        // Act
        await service.RecoverAsync([new BranchExecutionRequest(181, 81, BranchExecutionReason.Recovered)], CancellationToken.None);

        // Assert
        Assert.Equal([(171L, 71L, BranchExecutionReason.Recovered), (181L, 81L, BranchExecutionReason.Recovered)], runDispatcher.Requests);
        Assert.Contains(logger.Messages, message => message.Contains("Recovered 1 running branches and 1 Waiting branches whose wake may not have been dispatched.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_finalizes_runs_whose_last_branch_ended_before_the_previous_leader_finalized_them()
    {
        // Arrange: the old leader ended the run's last branch and stopped before finalizing the run.
        DateTime now = new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
        RunRow settled = CreateRunRow(91, RunStatus.Running);
        var runs = new Mock<IRunProvider>();
        runs.Setup(r => r.GetStuckRunsAsync(now, RunRecoveryService.SettledPageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([settled]);
        var branches = new Mock<IBranchProvider>();
        branches.Setup(b => b.GetAllByRunIdAsync(91, It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateBranch(1, 91) with { Status = BranchStatus.Completed }]);
        var cancellation = new Mock<IRunCancellationService>();
        var finalizer = new Mock<IRunFinalizer>();
        var sweeper = new SettledRunSweeper(runs.Object, branches.Object, cancellation.Object, finalizer.Object);
        var service = new RunRecoveryService(WorkerTestServices.Scopes(new RecordingBranchProvider([]), sweeper), new RecordingRunDispatcher(), new RecordingLogger<RunRecoveryService>(), clock: new FixedClock(now));

        // Act
        await service.RecoverAsync([], CancellationToken.None);

        // Assert: finalized at once, from its branches, rather than reaped as timed out half an hour later.
        finalizer.Verify(f => f.FinalizeAsync(91, It.IsAny<CancellationToken>()), Times.Once);
        cancellation.Verify(c => c.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_release_of_abandoned_claims_that_fails_faults_startup()
    {
        // Like a failed recovery: the inbound hub waits on Ready, so it must not open on claims left wedged.
        var claims = new Mock<IIdempotencyKeyProvider>();
        claims.Setup(c => c.ReleaseAbandonedClaimsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unreachable"));
        var service = new RunRecoveryService(WorkerTestServices.Scopes(claims.Object), new RecordingRunDispatcher(), new RecordingLogger<RunRecoveryService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReleaseAbandonedClaimsAsync(CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Ready);
    }

    [Fact]
    public async Task Collecting_woken_branches_reads_every_page_and_dispatches_nothing()
    {
        // Arrange
        WokenBranch[] woken = Enumerable.Range(1, RunRecoveryService.WokenPageSize + 5)
            .Select(id => new WokenBranch(id, 1000 + id, [1]))
            .ToArray();
        var branchProvider = new RecordingBranchProvider([], woken);
        var runDispatcher = new RecordingRunDispatcher();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(branchProvider), runDispatcher, new RecordingLogger<RunRecoveryService>());

        // Act
        IReadOnlyCollection<BranchExecutionRequest> found = await service.CollectWokenAsync(CancellationToken.None);

        // Assert
        Assert.Equal(woken.Select(branch => branch.Id), found.Select(request => request.BranchId));
        Assert.All(found, request => Assert.Equal(BranchExecutionReason.Recovered, request.Reason));
        Assert.Equal([0L, RunRecoveryService.WokenPageSize], branchProvider.WokenAfterIds);
        Assert.Empty(runDispatcher.Requests);
    }

    [Fact]
    public async Task Start_logs_zero_recovery_when_none()
    {
        // Arrange
        var branchProvider = new RecordingBranchProvider([]);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(branchProvider), runDispatcher, logger);

        // Act
        await service.RecoverAsync([], CancellationToken.None);

        // Assert
        Assert.Contains(logger.Messages, message => message.Contains("Recovered 0 running branches and 0 Waiting branches whose wake may not have been dispatched.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_sets_ready_task_completed()
    {
        // Arrange
        var branchProvider = new RecordingBranchProvider([]);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(branchProvider), runDispatcher, logger);

        Assert.False(service.Ready.IsCompleted);

        // Act
        await service.RecoverAsync([], CancellationToken.None);

        // Assert
        Assert.True(service.Ready.IsCompleted);
    }

    [Fact]
    public async Task Start_cancels_cancelling_or_failing_runs()
    {
        // Arrange
        var branches = new[]
        {
            Running(71, 171, "Cancelling"),
            Running(72, 172, "Failing")
        };
        var branchProvider = new RecordingBranchProvider(branches);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();

        var runProvider = new RecordingRunProvider();
        runProvider.AddRun(CreateRunRow(171, "Cancelling"));
        runProvider.AddRun(CreateRunRow(172, "Failing"));
        
        var cancellationService = new RecordingRunCancellationService();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(branchProvider, runProvider, cancellationService), runDispatcher, logger);

        // Act
        await service.RecoverAsync([], CancellationToken.None);

        // Assert: a Cancelling run gets the cancellation re-applied, which is what clears its bookmarks
        // and waiting branches. A Failing run must not be turned into a cancelled one, but its failure
        // is re-applied the same way, so a sibling parked on a signal cannot keep it alive after the
        // failover.
        Assert.Equal([171L], cancellationService.ReappliedCancellations);
        Assert.Equal([172L], cancellationService.ReappliedFailures);
    }

    [Fact]
    public async Task Start_reapplies_the_stop_of_failing_and_cancelling_runs_with_only_waiting_branches()
    {
        // Arrange: no Active branch anywhere, so the branch pass finds none of these runs. Their
        // remaining siblings are all Waiting, which used to keep them Failing or Cancelling for good.
        var branchProvider = new RecordingBranchProvider([]);
        var runDispatcher = new RecordingRunDispatcher();
        var logger = new RecordingLogger<RunRecoveryService>();

        var runProvider = new RecordingRunProvider();
        runProvider.AddRun(CreateRunRow(180, "Failing"));
        runProvider.AddRun(CreateRunRow(181, "Cancelling"));
        runProvider.AddRun(CreateRunRow(182, "Running"));

        var cancellationService = new RecordingRunCancellationService();
        var service = new RunRecoveryService(WorkerTestServices.Scopes(branchProvider, runProvider, cancellationService), runDispatcher, logger);

        // Act
        await service.RecoverAsync([], CancellationToken.None);

        // Assert: each stop re-applied once, by its own kind; a Running run is left alone.
        Assert.Equal([180L], cancellationService.ReappliedFailures);
        Assert.Equal([181L], cancellationService.ReappliedCancellations);
        Assert.Empty(runDispatcher.Requests);
        Assert.True(service.Ready.IsCompleted);
    }

    private static RunRow CreateRunRow(int id, string status)
    {
        return new RunRow
        {
            Id = id,
            RefId = Guid.NewGuid(),
            WorkflowDefinitionId = 1,
            WorkflowRefId = Guid.NewGuid(),
            WorkflowVersion = 1,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = "test-correlation",
            Status = status,
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 100m,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static RunningBranch Running(int id, int runId, string runStatus = "Running") =>
        new(id, runId, new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc), runStatus, null);

    private static BranchRow CreateBranch(int id, int runId)
    {
        return new BranchRow
        {
            Id = id,
            RefId = Guid.Parse($"00000000-0000-0000-0000-{id:D12}"),
            RunId = runId,
            ParentBranchId = null,
            ForkCohortId = null,
            NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Status = "Running",
            PendingTakePort = null,
            LocalJson = "{}",
            LastOutputJson = null,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            RowVersion = [1]
        };
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class RecordingBranchProvider(IReadOnlyCollection<RunningBranch> branches, IReadOnlyCollection<WokenBranch>? woken = null) : IBranchProvider
    {
        public List<long> WokenAfterIds { get; } = [];

        public Task<IReadOnlyCollection<WokenBranch>> GetWokenAsync(long afterId, int batchSize, CancellationToken ct)
        {
            WokenAfterIds.Add(afterId);
            IReadOnlyCollection<WokenBranch> page = (woken ?? []).Where(branch => branch.Id > afterId).OrderBy(branch => branch.Id).Take(batchSize).ToArray();
            return Task.FromResult(page);
        }

        public Task<IReadOnlyCollection<RunningBranch>> GetRunningBranchesAsync(long afterId, int batchSize, CancellationToken ct)
        {
            IReadOnlyCollection<RunningBranch> page = branches.Where(branch => branch.Id > afterId).OrderBy(branch => branch.Id).Take(batchSize).ToArray();
            return Task.FromResult(page);
        }

        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunDispatcher : IRunDispatcher
    {
        public List<(long RunId, long BranchId, BranchExecutionReason Reason)> Requests { get; } = [];

        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            Requests.Add((request.RunId, request.BranchId, request.Reason));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class RecordingRunProvider : IRunProvider
    {
        private readonly Dictionary<long, RunRow> _runs = new();

        public void AddRun(RunRow run)
        {
            _runs[run.Id] = run;
        }

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(_runs[runId]);
        public Task<IReadOnlyCollection<RunRow>> GetAllByStatusAsync(string status, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<RunRow>>(_runs.Values.Where(run => run.Status == status).ToArray());
        public Task<RunRow?> FindRowByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingRunCancellationService : IRunCancellationService
    {
        public List<long> CancelledRuns { get; } = new();
        public List<long> ReappliedCancellations { get; } = new();
        public List<long> ReappliedFailures { get; } = new();

        public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
        {
            ReappliedCancellations.Add(runId);
            return Task.FromResult(true);
        }

        public Task<bool> FailRunAsync(long runId, string reason, CancellationToken ct)
        {
            ReappliedFailures.Add(runId);
            return Task.FromResult(true);
        }

        public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public void CancelCts(long runId)
        {
            CancelledRuns.Add(runId);
        }
    }
}
