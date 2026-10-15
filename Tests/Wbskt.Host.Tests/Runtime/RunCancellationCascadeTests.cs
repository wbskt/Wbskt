using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

/// <summary>
/// A parent that stops takes its sub-workflow children with it. Before, a cancelled parent left its
/// children running to the end, and their completion found no bookmark to wake.
/// </summary>
public sealed class RunCancellationCascadeTests
{
    private readonly RunTree _runs = new();

    [Fact]
    public async Task Cancelling_a_parent_cancels_its_active_children_and_leaves_a_finished_one_alone()
    {
        RunRow parent = _runs.Add(1, "Running");
        RunRow running = _runs.Add(2, "Running", parent);
        RunRow failing = _runs.Add(3, "Failing", parent);
        RunRow finished = _runs.Add(4, "Succeeded", parent);

        bool cancelled = await Service().RequestCancellationAsync(parent.Id, "operator", CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal("Cancelling", _runs.Status(parent.Id));
        Assert.Equal("Cancelling", _runs.Status(running.Id));
        Assert.Equal("Cancelling", _runs.Status(failing.Id));
        Assert.Equal("Succeeded", _runs.Status(finished.Id));
        Assert.DoesNotContain(_runs.Transitions, t => t.RunId == finished.Id);
        // The reason names the parent, so a child's history says why it stopped.
        Assert.Equal($"Parent run {parent.RefId} was cancelled.", _runs.Reason(running.Id));
        Assert.Equal("operator", _runs.Reason(parent.Id));
    }

    [Fact]
    public async Task Cancelling_a_parent_cascades_to_its_grandchildren()
    {
        RunRow parent = _runs.Add(1, "Running");
        RunRow child = _runs.Add(2, "Running", parent);
        RunRow grandchild = _runs.Add(3, "Running", child);
        RunRow unrelated = _runs.Add(4, "Running");

        await Service().RequestCancellationAsync(parent.Id, "operator", CancellationToken.None);

        Assert.Equal("Cancelling", _runs.Status(child.Id));
        Assert.Equal("Cancelling", _runs.Status(grandchild.Id));
        Assert.Equal($"Parent run {child.RefId} was cancelled.", _runs.Reason(grandchild.Id));
        Assert.Equal("Running", _runs.Status(unrelated.Id));
    }

    [Fact]
    public async Task The_parent_is_stopped_before_its_children_are_cancelled()
    {
        // A child that finalizes as it is cancelled sends its completion to the parent; with the parent's
        // bookmarks already gone, that wakes nothing.
        RunRow parent = _runs.Add(1, "Running");
        RunRow child = _runs.Add(2, "Running", parent);

        await Service().RequestCancellationAsync(parent.Id, "operator", CancellationToken.None);

        Assert.Equal([parent.Id, child.Id], _runs.Transitions.Select(t => t.RunId));
    }

    [Fact]
    public async Task Failing_a_parent_cancels_its_children()
    {
        // Cancelled, not failed: the child did nothing wrong, its parent no longer wants its result.
        RunRow parent = _runs.Add(1, "Running");
        RunRow child = _runs.Add(2, "Running", parent);

        bool failed = await Service().FailRunAsync(parent.Id, "FAIL_RUN", CancellationToken.None);

        Assert.True(failed);
        Assert.Equal("Failing", _runs.Status(parent.Id));
        Assert.Equal("Cancelling", _runs.Status(child.Id));
        Assert.Equal($"Parent run {parent.RefId} was failed.", _runs.Reason(child.Id));
    }

    [Fact]
    public async Task A_child_that_cannot_be_cancelled_does_not_stop_the_others_or_the_parent()
    {
        RunRow parent = _runs.Add(1, "Running");
        RunRow broken = _runs.Add(2, "Running", parent);
        RunRow healthy = _runs.Add(3, "Running", parent);
        _runs.Broken.Add(broken.Id);

        bool cancelled = await Service().RequestCancellationAsync(parent.Id, "operator", CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal("Cancelling", _runs.Status(parent.Id));
        Assert.Equal("Running", _runs.Status(broken.Id));
        Assert.Equal("Cancelling", _runs.Status(healthy.Id));
    }

    [Fact]
    public async Task A_repeated_cancel_reaches_a_child_the_first_one_missed()
    {
        // The already-Cancelling path still cascades, so a redelivered cancel (or recovery re-applying a
        // cut-short stop) picks up a child left running.
        RunRow parent = _runs.Add(1, "Cancelling");
        RunRow child = _runs.Add(2, "Running", parent);

        bool cancelled = await Service().RequestCancellationAsync(parent.Id, "operator", CancellationToken.None);

        Assert.True(cancelled);
        Assert.Equal("Cancelling", _runs.Status(child.Id));
    }

    private RunCancellationService Service() => new(
        _runs,
        new RunJournal(Mock.Of<IHistoryEventProvider>(), TimeProvider.System),
        TimeProvider.System,
        Mock.Of<IBranchProvider>(),
        Mock.Of<IBookmarkProvider>(),
        new BranchTerminator(new UnlimitedRunCountersProvider(), new Lazy<IRunFinalizer>(() => new NoOpRunFinalizer())),
        new RunCancellationTokenRegistry());

    /// <summary>Runs keyed by id, related through <see cref="RunRow.ParentRunRefId"/> as the Runs table relates them.</summary>
    private sealed class RunTree : IRunProvider
    {
        private readonly Dictionary<long, RunRow> _rows = [];

        public List<(long RunId, string From, string To)> Transitions { get; } = [];
        public HashSet<long> Broken { get; } = [];

        public RunRow Add(int id, string status, RunRow? parent = null)
        {
            var row = new RunRow
            {
                Id = id,
                RefId = Guid.NewGuid(),
                WorkflowDefinitionId = 9,
                WorkflowRefId = Guid.NewGuid(),
                WorkflowVersion = 1,
                TriggerNodeId = Guid.NewGuid(),
                CorrelationKey = null,
                Status = status,
                StartedAt = DateTime.UtcNow,
                CompletedAt = null,
                CancellationRequestedAt = null,
                CancellationReason = null,
                CreditBudget = 100m,
                CreatedAt = DateTime.UtcNow,
                ParentRunRefId = parent?.RefId
            };
            _rows[id] = row;
            return row;
        }

        public string Status(long id) => _rows[id].Status;
        public string? Reason(long id) => _rows[id].CancellationReason;

        public Task<RunRow> GetByIdAsync(long runId, CancellationToken ct) => Task.FromResult(_rows[runId]);

        public Task<IReadOnlyCollection<RunRow>> GetActiveChildrenAsync(Guid parentRunRefId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<RunRow>>(_rows.Values
                .Where(r => r.ParentRunRefId == parentRunRefId && r.Status is "Running" or "Failing" or "Cancelling")
                .OrderBy(r => r.Id)
                .ToList());

        public Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
        {
            if (Broken.Contains(runId))
            {
                throw new InvalidOperationException("database unavailable");
            }

            RunRow row = _rows[runId];
            if (!string.Equals(row.Status, fromStatus, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            Transitions.Add((runId, fromStatus, toStatus));
            _rows[runId] = row with { Status = toStatus, CancellationReason = cancellationReason ?? row.CancellationReason };
            return Task.FromResult(true);
        }

        public Task<RunRow> CreateAsync(RunRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow?> FindRowByRefIdAsync(Guid refId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    }
}
