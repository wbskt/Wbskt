using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Host.Tests.Runtime.BranchResults;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.SharedVariables;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Runtime.BranchResults;

namespace Wbskt.Host.Tests.HostedServices;

/// <summary>
/// A branch parks in two writes - Waiting, then its bookmark - and one stranded between them used to
/// hold its run open forever. The orphan GC now fails such a branch with BOOKMARK_LOST.
/// </summary>
public sealed class StrandedBranchSweepTests
{
    private const int RunId = 42;
    private const long BranchId = 1001;
    private static readonly Guid WaitNodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Start = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task A_branch_stranded_by_a_failed_bookmark_insert_is_failed_and_its_run_ends()
    {
        // Arrange: the node parks, and the bookmark insert after the Waiting write throws.
        var state = new State();
        state.AddBranch(BranchId, BranchStatus.Active);
        state.FailBookmarkInserts = true;
        var harness = new Harness(state);

        // Act: the park dies half-way.
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.BranchLoop.RunAsync(RunId, BranchId, BranchExecutionReason.TriggerStarted, CancellationToken.None));

        // Assert: the branch is Waiting with nothing to wake it, and still counted against its run.
        Assert.Equal(BranchStatus.Waiting, state.Branches[BranchId].Status);
        Assert.Empty(state.Bookmarks);
        Assert.Equal(1, state.ActiveBranches);

        // Act: past the grace period, the GC sees it stranded on two passes in a row.
        state.Clock.Now = Start + Threshold + TimeSpan.FromMinutes(1);
        await harness.Gc.TickAsync(CancellationToken.None);
        Assert.Equal(BranchStatus.Waiting, state.Branches[BranchId].Status);
        await harness.Gc.TickAsync(CancellationToken.None);

        // Assert: failed through the ordinary failure path, released, and the run finalized.
        Assert.Equal(BranchStatus.Failed, state.Branches[BranchId].Status);
        Assert.Equal(0, state.ActiveBranches);
        Assert.Equal(RunStatus.Failed, state.Runs[RunId].Status);
        RunJournalEntry nodeFailed = Assert.Single(state.Journal, entry => entry.EventKind == HistoryEventKind.NodeFailed);
        Assert.Contains("BOOKMARK_LOST", JsonSerializer.Serialize(nodeFailed.Payload), StringComparison.Ordinal);
        RunJournalEntry branchFailed = Assert.Single(state.Journal, entry => entry.EventKind == HistoryEventKind.BranchFailed);
        Assert.Contains("BOOKMARK_LOST", JsonSerializer.Serialize(branchFailed.Payload), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sweep_leaves_young_branches_and_branches_with_a_bookmark_alone()
    {
        // Arrange: one branch parked properly long ago, one stranded but only just.
        var state = new State();
        state.AddBranch(BranchId, BranchStatus.Waiting, updatedAt: Start);
        state.AddBookmark(BranchId);
        state.AddBranch(BranchId + 1, BranchStatus.Waiting, updatedAt: Start + TimeSpan.FromMinutes(8));
        state.ActiveBranches = 2;
        state.Clock.Now = Start + TimeSpan.FromMinutes(10);
        var harness = new Harness(state);

        // Act
        await harness.Gc.TickAsync(CancellationToken.None);
        await harness.Gc.TickAsync(CancellationToken.None);

        // Assert
        Assert.Equal(Start + TimeSpan.FromMinutes(5), state.LastStrandedCutoff);
        Assert.Equal(BranchStatus.Waiting, state.Branches[BranchId].Status);
        Assert.Equal(BranchStatus.Waiting, state.Branches[BranchId + 1].Status);
        Assert.Equal(2, state.ActiveBranches);
        Assert.Equal(RunStatus.Running, state.Runs[RunId].Status);
    }

    [Fact]
    public async Task The_sweep_does_not_fail_a_branch_on_its_first_sighting()
    {
        // Arrange: a wake claims (deletes) the bookmark before the woken branch is written, so for that
        // moment a long-parked branch looks stranded. One sighting must not be enough.
        var state = new State();
        state.AddBranch(BranchId, BranchStatus.Waiting, updatedAt: Start);
        state.Clock.Now = Start + TimeSpan.FromHours(1);
        var harness = new Harness(state);

        // Act
        await harness.Gc.TickAsync(CancellationToken.None);
        state.Touch(BranchId);
        await harness.Gc.TickAsync(CancellationToken.None);

        // Assert: the branch was written between the passes, so it is a new sighting, not a second one.
        Assert.Equal(BranchStatus.Waiting, state.Branches[BranchId].Status);
        Assert.Equal(1, state.ActiveBranches);
    }

    [Fact]
    public async Task The_failer_leaves_a_branch_that_got_its_bookmark_or_moved_on_since_the_sweep_read_it()
    {
        // Arrange
        var state = new State();
        state.AddBranch(BranchId, BranchStatus.Waiting, updatedAt: Start);
        state.AddBranch(BranchId + 1, BranchStatus.Waiting, updatedAt: Start);
        state.ActiveBranches = 2;
        BranchRow seenWithBookmarkLater = state.Branches[BranchId];
        BranchRow seenBeforeWake = state.Branches[BranchId + 1];
        state.AddBookmark(BranchId);
        state.Touch(BranchId + 1);
        var harness = new Harness(state);

        // Act
        bool failedFirst = await harness.Failer.FailAsync(seenWithBookmarkLater, CancellationToken.None);
        bool failedSecond = await harness.Failer.FailAsync(seenBeforeWake, CancellationToken.None);

        // Assert
        Assert.False(failedFirst);
        Assert.False(failedSecond);
        Assert.Equal(BranchStatus.Waiting, state.Branches[BranchId].Status);
        Assert.Equal(BranchStatus.Waiting, state.Branches[BranchId + 1].Status);
        Assert.Equal(2, state.ActiveBranches);
    }

    [Fact]
    public async Task A_stranded_branch_of_a_failing_run_gets_the_run_failure_reapplied()
    {
        // Arrange
        var state = new State();
        state.AddBranch(BranchId, BranchStatus.Waiting, updatedAt: Start);
        state.Runs[RunId] = state.Runs[RunId] with { Status = RunStatus.Failing };
        var cancellation = new RecordingRunCancellationService();
        var harness = new Harness(state, cancellation);

        // Act
        bool handled = await harness.Failer.FailAsync(state.Branches[BranchId], CancellationToken.None);

        // Assert: the run's own stop cancels the branch; it is not failed a second time.
        Assert.True(handled);
        Assert.Equal([(long)RunId], cancellation.Failures);
        Assert.Empty(cancellation.Cancellations);
        Assert.Equal(BranchStatus.Waiting, state.Branches[BranchId].Status);
    }

    [Fact]
    public async Task A_woken_branch_that_has_already_ended_is_not_run_again()
    {
        // Arrange: the sweep failed the branch, and a late wake for it arrives afterwards.
        var state = new State();
        state.AddBranch(BranchId, BranchStatus.Failed);
        state.ActiveBranches = 0;
        var harness = new Harness(state);

        // Act
        await harness.BranchLoop.RunAsync(RunId, BranchId, BranchExecutionReason.BookmarkResumed, CancellationToken.None);

        // Assert: no node ran, nothing was released a second time.
        Assert.Equal(0, harness.WaitExecutor.Calls);
        Assert.Equal(0, state.ActiveBranches);
        Assert.Empty(state.Journal);
    }

    private sealed class Harness
    {
        public Harness(State state, IRunCancellationService? cancellation = null)
        {
            var branchProvider = new InMemoryBranchProvider(state);
            var runProvider = new InMemoryRunProvider(state);
            var counters = new InMemoryRunCountersProvider(state);
            var bookmarkProvider = new InMemoryBookmarkProvider(state);
            var journal = new InMemoryJournal(state);
            var cache = new StaticDefinitionCache(state.Definition);
            var runDispatcher = new RecordingRunDispatcher(new List<string>());
            var idGenerator = new SequentialIdGenerator();
            var runCancellationService = cancellation ?? new NoOpRunCancellationService();
            var finalizer = new RunFinalizer(runProvider, branchProvider, journal, new NoOpDrainer(), new NullRunCompletedPublisher(), bookmarkProvider, new NoOpCompletionHook(), state.Clock, new RunCancellationTokenRegistry(), new NoOpJoinAggregatorProvider(), cache);
            var terminator = new BranchTerminator(counters, new Lazy<IRunFinalizer>(() => finalizer));

            WaitExecutor = new WaitExecutor(state.Clock);
            BranchLoop = TestBranchLoop.Create(branchProvider, runProvider, counters, bookmarkProvider, journal, cache, new NodeExecutorRegistry([WaitExecutor]), runDispatcher, new InMemoryIdempotencyKeyProvider(), state.Clock, idGenerator, terminator, runCancellationService, new NoOpCompensationOrchestrator(), new DefaultCreditCostCalculator(), new NoOpJoinAggregatorProvider());

            var continueHandler = new ContinueResultHandler(branchProvider, new BranchEndings(branchProvider, journal, terminator));
            var resultDispatcher = new BranchResultDispatcher(
            [
                continueHandler,
                new FailResultHandler(branchProvider, counters, runDispatcher, idGenerator, state.Clock, runCancellationService, new NoOpCompensationOrchestrator(), new NoOpJoinAggregatorProvider(), journal, terminator, continueHandler)
            ]);
            Failer = new StrandedBranchFailer(branchProvider, bookmarkProvider, runProvider, cache, resultDispatcher, runCancellationService, runDispatcher, journal, terminator);

            Gc = new BookmarkOrphanGc(
                state.Clock,
                new ActiveLeadership(),
                WorkerTestServices.Scopes(branchProvider, bookmarkProvider, Failer),
                NullLogger<BookmarkOrphanGc>.Instance,
                WorkerTestServices.Options(new WorkflowEngineOptions { StrandedBranchThreshold = Threshold }));
        }

        public BranchLoop BranchLoop { get; }

        public StrandedBranchFailer Failer { get; }

        public BookmarkOrphanGc Gc { get; }

        public WaitExecutor WaitExecutor { get; }
    }

    private sealed class State
    {
        private int _version;

        public State()
        {
            Definition = new WorkflowDefinition(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                1,
                9,
                "stranded-branch",
                null,
                true,
                [new TestNode { NodeId = WaitNodeId, Name = "wait", Ports = [], KindValue = "test:wait" }],
                [],
                Array.Empty<SharedVariableDeclaration>(),
                Start,
                7);
            Runs[RunId] = new RunRow
            {
                Id = RunId,
                RefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-cccccccccccc"),
                WorkflowDefinitionId = 9,
                WorkflowRefId = Definition.WorkflowRefId,
                WorkflowVersion = 1,
                TriggerNodeId = WaitNodeId,
                CorrelationKey = "corr",
                Status = RunStatus.Running,
                StartedAt = Start,
                CompletedAt = null,
                CancellationRequestedAt = null,
                CancellationReason = null,
                CreditBudget = 100m,
                CreatedAt = Start
            };
        }

        public WorkflowDefinition Definition { get; }
        public MutableClock Clock { get; } = new() { Now = Start };
        public Dictionary<long, RunRow> Runs { get; } = [];
        public Dictionary<long, BranchRow> Branches { get; } = [];
        public List<BookmarkRow> Bookmarks { get; } = [];
        public List<RunJournalEntry> Journal { get; } = [];
        public int ActiveBranches { get; set; } = 1;
        public bool FailBookmarkInserts { get; set; }
        public DateTime? LastStrandedCutoff { get; set; }

        public byte[] NextVersion() => BitConverter.GetBytes(++_version);

        public void AddBranch(long id, string status, DateTime? updatedAt = null)
        {
            Branches[id] = new BranchRow
            {
                Id = checked((int)id),
                RefId = Guid.Parse($"bbbbbbbb-bbbb-bbbb-bbbb-{id:D12}"),
                RunId = RunId,
                ParentBranchId = null,
                ForkCohortId = null,
                NodeId = WaitNodeId,
                Status = status,
                PendingTakePort = null,
                LocalJson = "{}",
                LastOutputJson = null,
                CompensationStackJson = null,
                CreatedAt = Start,
                UpdatedAt = updatedAt ?? Clock.Now,
                RowVersion = NextVersion()
            };
        }

        public void AddBookmark(long branchId)
        {
            Bookmarks.Add(new BookmarkRow
            {
                Id = Bookmarks.Count + 1,
                RefId = Guid.NewGuid(),
                RunId = RunId,
                BranchRefId = Branches[branchId].RefId,
                NodeId = WaitNodeId,
                WakeConditionKind = "signal",
                MatchKey = "signal:x",
                WakeConditionJson = "{}",
                ExpiresAt = null,
                TtlPort = null,
                CreatedAt = Start
            });
        }

        public void Touch(long branchId)
        {
            Branches[branchId] = Branches[branchId] with { RowVersion = NextVersion() };
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTime Now { get; set; }

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed class WaitExecutor(TimeProvider clock) : INodeExecutor
    {
        public int Calls { get; private set; }

        public string Kind => "test:wait";

        public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.WaitForBookmark(
                new TimerWakeCondition(clock.GetUtcNow().UtcDateTime.AddDays(1)),
                new Dictionary<string, JsonElement>()));
        }
    }

    private sealed class InMemoryBranchProvider(State state) : IBranchProvider
    {
        public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct) => Task.FromResult(state.Branches[branchId]);
        public Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => Task.FromResult(state.Branches.Values.Single(branch => branch.RefId == refId));
        public Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BranchRow>>(state.Branches.Values.Where(branch => branch.RunId == runId).ToArray());
        public Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct) => throw new NotSupportedException();

        // What Branch_GetWaitingWithoutBookmark selects.
        public Task<IReadOnlyCollection<BranchRow>> GetWaitingWithoutBookmarkAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            state.LastStrandedCutoff = cutoffUtc;
            IReadOnlyCollection<BranchRow> rows = state.Branches.Values
                .Where(branch => branch.Status == BranchStatus.Waiting
                    && branch.UpdatedAt < cutoffUtc
                    && !RunStatus.IsTerminal(state.Runs[branch.RunId].Status)
                    && !state.Bookmarks.Any(bookmark => bookmark.RunId == branch.RunId && bookmark.BranchRefId == branch.RefId))
                .OrderBy(branch => branch.UpdatedAt)
                .Take(batchSize)
                .ToArray();
            return Task.FromResult(rows);
        }

        public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct) =>
            Write(branchId, branch => branch with { NodeId = currentNodeId, Status = status, LocalJson = localJson, LastOutputJson = lastOutputJson, PendingTakePort = null });

        public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct) => Write(branchId, branch => branch with { Status = BranchStatus.Completed });

        public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct) => Write(branchId, branch => branch with { Status = BranchStatus.Failed, LastOutputJson = lastOutputJson });

        private Task<BranchRow> Write(long branchId, Func<BranchRow, BranchRow> change)
        {
            BranchRow updated = change(state.Branches[branchId]) with { UpdatedAt = state.Clock.Now, RowVersion = state.NextVersion() };
            state.Branches[branchId] = updated;
            return Task.FromResult(updated);
        }
    }

    private sealed class InMemoryRunProvider(State state) : IRunProvider
    {
        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(state.Runs[runId]);
        public Task<RunRow?> FindRowByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();

        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct)
        {
            RunRow updated = state.Runs[runId] with { Status = status, CompletedAt = completedAt };
            state.Runs[runId] = updated;
            return Task.FromResult((true, updated));
        }
    }

    private sealed class InMemoryRunCountersProvider(State state) : IRunCountersProvider
    {
        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct) =>
            Task.FromResult(new RunCountersRow { RunId = runId, ActiveBranchCount = state.ActiveBranches, CreditsConsumed = 0m, UpdatedAt = state.Clock.Now });

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => Task.FromResult(state.ActiveBranches += delta);

        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            state.ActiveBranches = Math.Max(0, state.ActiveBranches - delta);
            return Task.FromResult(state.ActiveBranches);
        }

        public Task<bool> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => Task.FromResult(true);
        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class InMemoryBookmarkProvider(State state) : IBookmarkProvider
    {
        public Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct)
        {
            if (state.FailBookmarkInserts)
            {
                throw new InvalidOperationException("The bookmark insert failed.");
            }

            state.Bookmarks.Add(row);
            return Task.FromResult(row);
        }

        public Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<BookmarkRow>>(state.Bookmarks.Where(bookmark => bookmark.RunId == runId).ToArray());
        public Task<bool> TryClaimAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<BookmarkRow>> ClaimDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CountAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteOrphansAsync(CancellationToken ct) => Task.FromResult(0);

        public Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
        {
            state.Bookmarks.RemoveAll(bookmark => bookmark.RunId == runId);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryJournal(State state) : IRunJournal
    {
        public Task AppendAsync(RunJournalEntry entry, CancellationToken ct)
        {
            state.Journal.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<HistoryEventRow>> ReadAllAsync(int runId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StaticDefinitionCache(WorkflowDefinition definition) : IWorkflowDefinitionCache
    {
        public Task<WorkflowDefinition> GetAsync(int workflowDefinitionId, CancellationToken ct) => Task.FromResult(definition);
        public void Invalidate(int workflowDefinitionId) { }
    }

    private sealed class RecordingRunCancellationService : IRunCancellationService
    {
        public List<long> Cancellations { get; } = [];
        public List<long> Failures { get; } = [];

        public Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
        {
            Cancellations.Add(runId);
            return Task.FromResult(true);
        }

        public Task<bool> FailRunAsync(long runId, string reason, CancellationToken ct)
        {
            Failures.Add(runId);
            return Task.FromResult(true);
        }

        public Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class ActiveLeadership : ILeadershipGate
    {
        public bool IsActive => true;
    }

    private sealed class NoOpDrainer : IPendingTriggerEventDrainer
    {
        public Task DrainAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoOpCompletionHook : ISubWorkflowCompletionHook
    {
        public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct) => Task.CompletedTask;
    }
}
