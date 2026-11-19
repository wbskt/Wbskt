using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;

namespace Wbskt.Host.Tests.HostedServices;

public sealed class RunReaperTests
{
    [Fact]
    public async Task Tick_skips_when_lease_not_held()
    {
        // Arrange
        var leadership = new FixedLeadershipGate(false);
        var runProvider = new RecordingRunProvider([]);
        var cancellationService = new RecordingRunCancellationService();
        var finalizer = new RecordingRunFinalizer();
        RunReaper reaper = CreateReaper(leadership, runProvider, Branches(), cancellationService, finalizer);

        // Act
        await reaper.TickAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, leadership.Checks);
        Assert.Equal(0, runProvider.GetStuckRunsCalls);
        Assert.Empty(cancellationService.Requests);
        Assert.Empty(finalizer.FinalizedRunIds);
    }

    [Fact]
    public async Task Tick_finalizes_runs_with_no_work_left_without_cancelling_them()
    {
        // Arrange: the engine stopped after the last branch ended and before the run was finalized.
        var runProvider = new RecordingRunProvider([CreateRun(51), CreateRun(52, RunStatus.Cancelling), CreateRun(53, RunStatus.Failing)]);
        var cancellationService = new RecordingRunCancellationService();
        var finalizer = new RecordingRunFinalizer();
        RunReaper reaper = CreateReaper(new FixedLeadershipGate(true), runProvider, Branches((51, BranchStatus.Completed), (51, BranchStatus.Failed)), cancellationService, finalizer);

        // Act
        await reaper.TickAsync(CancellationToken.None);

        // Assert: the finalizer takes the status from the branches, as the normal path would have.
        Assert.Equal((new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc), 100), runProvider.LastRequest);
        Assert.Empty(cancellationService.Requests);
        Assert.Equal([51L, 52L, 53L], finalizer.FinalizedRunIds);
    }

    [Fact]
    public async Task Tick_times_out_a_running_run_whose_branches_all_ended_cancelled()
    {
        // Arrange
        var runProvider = new RecordingRunProvider([CreateRun(61)]);
        var cancellationService = new RecordingRunCancellationService();
        var finalizer = new RecordingRunFinalizer();
        RunReaper reaper = CreateReaper(new FixedLeadershipGate(true), runProvider, Branches((61, BranchStatus.Cancelled)), cancellationService, finalizer);

        // Act
        await reaper.TickAsync(CancellationToken.None);

        // Assert: nothing earned an outcome, so it must not finalize as Succeeded.
        Assert.Equal([(61L, SettledRunSweeper.ReaperTimeoutReason)], cancellationService.Requests);
        Assert.Equal([61L], finalizer.FinalizedRunIds);
    }

    [Fact]
    public async Task Tick_does_nothing_when_no_stuck_runs()
    {
        // Arrange
        var cancellationService = new RecordingRunCancellationService();
        var finalizer = new RecordingRunFinalizer();
        RunReaper reaper = CreateReaper(new FixedLeadershipGate(true), new RecordingRunProvider([]), Branches(), cancellationService, finalizer);

        // Act
        await reaper.TickAsync(CancellationToken.None);

        // Assert
        Assert.Empty(cancellationService.Requests);
        Assert.Empty(finalizer.FinalizedRunIds);
    }

    [Fact]
    public async Task The_sweep_reads_further_batches_and_stops_on_runs_that_did_not_move()
    {
        // Arrange: a full batch that the finalizer could not move comes back on the next read.
        var runProvider = new RecordingRunProvider([CreateRun(71), CreateRun(72)]);
        var finalizer = new RecordingRunFinalizer();
        var sweeper = new SettledRunSweeper(runProvider, Branches((71, BranchStatus.Completed), (72, BranchStatus.Completed)), new RecordingRunCancellationService(), finalizer);

        // Act
        int finalized = await sweeper.FinalizeAsync(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc), batchSize: 2, CancellationToken.None);

        // Assert
        Assert.Equal(2, finalized);
        Assert.Equal(2, runProvider.GetStuckRunsCalls);
        Assert.Equal([71L, 72L], finalizer.FinalizedRunIds);
    }

    private static RunReaper CreateReaper(FixedLeadershipGate leadership, RecordingRunProvider runProvider, IBranchProvider branchProvider, RecordingRunCancellationService cancellationService, RecordingRunFinalizer finalizer)
    {
        var sweeper = new SettledRunSweeper(runProvider, branchProvider, cancellationService, finalizer);
        return new RunReaper(new FixedClock(), leadership, WorkerTestServices.Scopes(sweeper), NullLogger<RunReaper>.Instance, WorkerTestServices.Options());
    }

    private static IBranchProvider Branches(params (int RunId, string Status)[] branches)
    {
        var provider = new Mock<IBranchProvider>();
        provider.Setup(p => p.GetAllByRunIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int runId, CancellationToken _) => branches
                .Where(branch => branch.RunId == runId)
                .Select((branch, index) => CreateBranch(index + 1, runId, branch.Status))
                .ToArray());
        return provider.Object;
    }

    private static BranchRow CreateBranch(int id, int runId, string status)
    {
        return new BranchRow
        {
            Id = id,
            RefId = Guid.NewGuid(),
            RunId = runId,
            ParentBranchId = null,
            ForkCohortId = null,
            NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Status = status,
            PendingTakePort = null,
            LocalJson = "{}",
            CreatedAt = new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
            RowVersion = []
        };
    }

    private static RunRow CreateRun(int id, string status = RunStatus.Running)
    {
        return new RunRow
        {
            Id = id,
            RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowDefinitionId = 3,
            WorkflowRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            WorkflowVersion = 1,
            TriggerNodeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            CorrelationKey = "corr",
            Status = status,
            StartedAt = new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc),
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 10,
            CreatedAt = new DateTime(2026, 5, 26, 11, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => UtcNow;

        public DateTime UtcNow => new(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
    }

    private sealed class FixedLeadershipGate(bool isActive) : ILeadershipGate
    {
        public int Checks { get; private set; }

        public bool IsActive
        {
            get
            {
                Checks++;
                return isActive;
            }
        }
    }

    private sealed class RecordingRunProvider(IReadOnlyCollection<RunRow> runs) : IRunProvider
    {
        public int GetStuckRunsCalls { get; private set; }
        public (DateTime CutoffUtc, int BatchSize)? LastRequest { get; private set; }

        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow?> FindRowByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            GetStuckRunsCalls++;
            LastRequest = (cutoffUtc, batchSize);
            return Task.FromResult(runs);
        }
    }

    private sealed class RecordingRunCancellationService : IRunCancellationService
    {
        public List<(long RunId, string Reason)> Requests { get; } = [];

        public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
        {
            Requests.Add((runId, reason));
            return Task.FromResult(true);
        }

        public Task<bool> FailRunAsync(long runId, string reason, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class RecordingRunFinalizer : IRunFinalizer
    {
        public List<long> FinalizedRunIds { get; } = [];

        public Task FinalizeAsync(long runId, CancellationToken ct)
        {
            FinalizedRunIds.Add(runId);
            return Task.CompletedTask;
        }
    }
}


