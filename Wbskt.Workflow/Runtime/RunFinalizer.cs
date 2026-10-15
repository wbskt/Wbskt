using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunFinalizer : IRunFinalizer
{
    private readonly IRunProvider _runProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IRunJournal _journal;
    private readonly IPendingTriggerEventDrainer _pendingTriggerEventDrainer;
    private readonly IRunCompletedPublisher _runCompletedPublisher;
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IJoinAggregatorProvider _joinAggregatorProvider;
    private readonly IWorkflowDefinitionCache _definitionCache;
    private readonly ISubWorkflowCompletionHook _completionHook;
    private readonly TimeProvider _clock;
    // The registry, not IRunCancellationService: the cancellation service finalizes a run left idle by
    // a cancel, so depending on it here would make the two depend on each other.
    private readonly RunCancellationTokenRegistry _tokenRegistry;
    private readonly WorkflowMetrics? _workflowMetrics;
    private readonly ILogger<RunFinalizer>? _logger;

    public RunFinalizer(
        IRunProvider runProvider,
        IBranchProvider branchProvider,
        IRunJournal journal,
        IPendingTriggerEventDrainer pendingTriggerEventDrainer,
        IRunCompletedPublisher runCompletedPublisher,
        IBookmarkProvider bookmarkProvider,
        ISubWorkflowCompletionHook completionHook,
        TimeProvider clock,
        RunCancellationTokenRegistry tokenRegistry,
        IJoinAggregatorProvider joinAggregatorProvider,
        IWorkflowDefinitionCache definitionCache,
        WorkflowMetrics? workflowMetrics = null,
        ILogger<RunFinalizer>? logger = null)
    {
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _journal = journal;
        _pendingTriggerEventDrainer = pendingTriggerEventDrainer;
        _runCompletedPublisher = runCompletedPublisher;
        _bookmarkProvider = bookmarkProvider;
        _completionHook = completionHook;
        _clock = clock;
        _workflowMetrics = workflowMetrics;
        _tokenRegistry = tokenRegistry;
        _joinAggregatorProvider = joinAggregatorProvider;
        _definitionCache = definitionCache;
        _logger = logger;
    }

    /// <summary>
    /// The workspace a run belongs to, for metric tagging. Runs do not carry it - it lives on the
    /// definition - and this is only used to label a counter, so a lookup failure degrades to 0
    /// rather than derailing finalization.
    /// </summary>
    private async Task<int> ResolveWorkspaceIdAsync(int workflowDefinitionId, CancellationToken ct)
    {
        try
        {
            return (await _definitionCache.GetAsync(workflowDefinitionId, ct)).WorkspaceId;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not resolve the workspace for definition {DefinitionId} while tagging run metrics.", workflowDefinitionId);
            return 0;
        }
    }

    public async Task FinalizeAsync(long runId, CancellationToken ct)
    {
        RunRow run = await _runProvider.GetByIdAsync(runId, ct);
        IReadOnlyCollection<BranchRow> branches = await _branchProvider.GetAllByRunIdAsync(run.Id, ct);
        string terminalStatus = DetermineTerminalStatus(run, branches);
        DateTime completedAt = _clock.GetUtcNow().UtcDateTime;

        (bool transitioned, RunRow updatedRun) = await _runProvider.SetTerminalAsync(runId, terminalStatus, completedAt, ct);
        if (!transitioned)
        {
            _logger?.LogDebug("Run {RunId} was already terminal ({Status}); skipping finalize side effects.", runId, updatedRun.Status);
            return;
        }

        _workflowMetrics?.RecordRunCompleted(
            updatedRun.WorkflowRefId.ToString(),
            terminalStatus,
            await ResolveWorkspaceIdAsync(updatedRun.WorkflowDefinitionId, ct));
        await _journal.AppendAsync(
            new RunJournalEntry(updatedRun.Id, HistoryEventKind.RunFinalized)
            {
                Payload = new { Status = terminalStatus },
                // A run that ended badly is kept as long as the failures that caused it (see HistoryEventKind).
                Severity = RunStatus.IsFailure(terminalStatus) ? HistoryEventKind.SeverityWarn : HistoryEventKind.SeverityInfo
            },
            ct);
        
        // The run just freed its correlation slot, so release exactly one event that a Queue
        // concurrency policy parked behind it. One, not all: draining more would re-enter the
        // concurrency enforcer while the newly started run is active and simply re-queue them.
        await _pendingTriggerEventDrainer.DrainAsync(updatedRun.WorkflowRefId, updatedRun.TriggerNodeId, updatedRun.CorrelationKey ?? string.Empty, ct);

        // Published through an abstraction rather than IEventBus directly so Wbskt.Workflow carries no
        // event-bus dependency; the engine host supplies the implementation.
        await _runCompletedPublisher.PublishAsync(updatedRun, terminalStatus, ct);
        await _bookmarkProvider.DeleteAllByRunIdAsync(updatedRun.Id, ct);

        // Join cohorts are kept for the life of the run (they are what you inspect when a cohort
        // looks stuck) and collected here, alongside the bookmarks.
        await _joinAggregatorProvider.DeleteAllByRunIdAsync(updatedRun.Id, ct);

        // The output a control:end node recorded goes with the status, whatever the status is: a parent
        // that branches on childResult.status decides for itself whether to trust it.
        await _completionHook.OnRunCompletedAsync(updatedRun.RefId, terminalStatus, updatedRun.OutputJson, ct);
        _tokenRegistry.Remove(updatedRun.Id);
    }

    private static string DetermineTerminalStatus(RunRow run, IReadOnlyCollection<BranchRow> branches)
    {
        if (string.Equals(run.Status, RunStatus.Cancelling, StringComparison.Ordinal))
        {
            if (string.Equals(run.CancellationReason, "OUT_OF_CREDITS", StringComparison.Ordinal))
            {
                return RunStatus.OutOfCredits;
            }

            // §5.4: a branch that actually completed despite the cancellation request means the
            // run only partially made it out - anything else (nothing completed, whether branches
            // were cancelled, failed, or the request simply beat every branch to the punch) is a
            // clean Cancelled.
            bool hasCancelledBranch = branches.Any(branch => string.Equals(branch.Status, BranchStatus.Cancelled, StringComparison.Ordinal));
            bool hasCompletedDespiteCancellation = branches.Any(branch => string.Equals(branch.Status, BranchStatus.Completed, StringComparison.Ordinal));
            return hasCancelledBranch && hasCompletedDespiteCancellation ? RunStatus.PartiallyFailed : RunStatus.Cancelled;
        }

        // A run that failed by its own rule (onFailure FailRun, a control:failRun node, FailFast) is
        // 'Failing', and ends Failed however its siblings ended - completed, or cancelled when the
        // failure stopped them. It must say Failed, not Cancelled: the console shows the status and
        // the reason, and "Cancelled" for a FailRun used to read as if someone had pressed cancel.
        // Only an explicit cancel taken while it was failing moves it on to 'Cancelling' above.
        if (string.Equals(run.Status, RunStatus.Failing, StringComparison.Ordinal))
        {
            return RunStatus.Failed;
        }

        bool hasCompleted = branches.Any(branch => string.Equals(branch.Status, BranchStatus.Completed, StringComparison.Ordinal));
        bool hasFailed = branches.Any(branch => string.Equals(branch.Status, BranchStatus.Failed, StringComparison.Ordinal));

        if (hasFailed && hasCompleted)
        {
            return RunStatus.PartiallyFailed;
        }

        if (hasFailed)
        {
            return RunStatus.Failed;
        }

        return RunStatus.Succeeded;
    }
}
