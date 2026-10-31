using Moq;
using Wbskt.Host.Tests.Runtime.BranchResults;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;
using static Wbskt.Host.Tests.Runtime.BranchResults.BranchResultTestData;

namespace Wbskt.Host.Tests.Runtime;

/// <summary>
/// A branch ends once. Two loops can reach the end of the same branch - a cancel racing a bookmark
/// wake, a recovery requeue racing a slow node on the old leader - and only the first may record the
/// end and release the branch. These tests drive the real <see cref="BranchTerminator"/> over a counter
/// that, like RunCounters_DecrementActiveBranches, is clamped at zero.
/// </summary>
public sealed class BranchEndingsTests
{
    private static readonly Guid NodeA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly List<string> _log = [];
    private readonly ClampedCounters _counters = new();
    private readonly Mock<IRunFinalizer> _finalizer = new();
    private readonly RecordingRunJournal _journal;
    private readonly BranchTerminator _terminator;
    private readonly WarningRecorder<BranchEndings> _logger = new();

    public BranchEndingsTests()
    {
        _journal = new RecordingRunJournal(_log);
        _terminator = new BranchTerminator(_counters, new Lazy<IRunFinalizer>(() => _finalizer.Object));
    }

    [Fact]
    public async Task Completing_a_branch_twice_records_and_releases_it_once()
    {
        _counters.Active = 3;
        var branches = new RecordingBranchProvider(_log, Branch(NodeA));
        BranchEndings endings = Endings(branches);

        await endings.CompleteAsync(RunId, BranchId, BranchRefId, CancellationToken.None);
        await endings.CompleteAsync(RunId, BranchId, BranchRefId, CancellationToken.None);

        Assert.Equal(2, _counters.Active);
        Assert.Equal(1, _counters.Decrements);
        Assert.Equal(HistoryEventKind.BranchCompleted, Assert.Single(_journal.Entries).EventKind);
        Assert.Equal(new[] { "completed", $"journal:{HistoryEventKind.BranchCompleted}", $"end-refused:{BranchStatus.Completed}" }, _log);
        Assert.Single(_logger.Warnings);
        _finalizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_cancelled_branch_ended_again_by_a_late_wake_stays_cancelled()
    {
        // The cancel race: the run's cancel marks the waiting branch Cancelled and releases it while a
        // bookmark wake for it is in flight; the woken loop then reaches the end of the branch too.
        _counters.Active = 3;
        var branches = new RecordingBranchProvider(_log, Branch(NodeA) with { Status = BranchStatus.Waiting });
        BranchEndings endings = Endings(branches);

        await endings.CancelAsync(RunId, BranchId, branches.Current, CancellationToken.None);
        await endings.CompleteAsync(RunId, BranchId, BranchRefId, CancellationToken.None);

        Assert.Equal(BranchStatus.Cancelled, branches.Current.Status);
        Assert.Equal(2, _counters.Active);
        Assert.Equal(HistoryEventKind.BranchCancelled, Assert.Single(_journal.Entries).EventKind);
        Assert.Single(_logger.Warnings);
    }

    [Fact]
    public async Task With_three_live_branches_a_double_end_does_not_finalise_the_run_while_one_still_runs()
    {
        // Before the guard the second end released the branch again: 3 -> 2 -> 1, and the next branch
        // to end took the count to zero and finalised the run with the third branch still running.
        _counters.Active = 3;
        var first = new RecordingBranchProvider(_log, Branch(NodeA));
        var second = new RecordingBranchProvider(_log, Branch(NodeA) with { Id = (int)BranchId + 1, RefId = Guid.NewGuid() });

        await Endings(first).CompleteAsync(RunId, BranchId, BranchRefId, CancellationToken.None);
        await Endings(first).CancelAsync(RunId, BranchId, first.Current, CancellationToken.None);
        await Endings(second).CompleteAsync(RunId, BranchId + 1, second.Current.RefId, CancellationToken.None);

        Assert.Equal(1, _counters.Active);
        _finalizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ending_the_last_live_branch_once_still_finalises_the_run()
    {
        _counters.Active = 1;
        var branches = new RecordingBranchProvider(_log, Branch(NodeA));

        await Endings(branches).CompleteAsync(RunId, BranchId, BranchRefId, CancellationToken.None);
        await Endings(branches).CompleteAsync(RunId, BranchId, BranchRefId, CancellationToken.None);

        _finalizer.Verify(finalizer => finalizer.FinalizeAsync(RunId, It.IsAny<CancellationToken>()), Times.Once);
    }


    private BranchEndings Endings(IBranchProvider branches) => new(branches, _journal, _terminator, _logger);

    /// <summary>The active-branch count, clamped at zero as RunCounters_DecrementActiveBranches is.</summary>
    private sealed class ClampedCounters : IRunCountersProvider
    {
        public int Active { get; set; }

        public int Decrements { get; private set; }

        public Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
        {
            Decrements++;
            Active = Math.Max(0, Active - delta);
            return Task.FromResult(Active);
        }

        public Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct) => throw new NotSupportedException();

        public Task<RunCountersRow> GetByRunIdAsync(int runId, CancellationToken ct) => throw new NotSupportedException();

        public Task<CreditCharge?> TryChargeAsync(int runId, decimal cost, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> SumActiveBranchesAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
