using System.Text.Json;
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
    private readonly IHistoryEventProvider _historyEventProvider;
    private readonly IPendingTriggerEventDrainer _pendingTriggerEventDrainer;
    private readonly IRunCompletedPublisher _runCompletedPublisher;
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IJoinAggregatorProvider? _joinAggregatorProvider;
    private readonly ISubWorkflowCompletionHook _completionHook;
    private readonly IClock _clock;
    private readonly IRunCancellationService? _runCancellationService;
    private readonly WorkflowMetrics? _workflowMetrics;
    private readonly ILogger<RunFinalizer>? _logger;

    public RunFinalizer(
        IRunProvider runProvider,
        IBranchProvider branchProvider,
        IHistoryEventProvider historyEventProvider,
        IPendingTriggerEventDrainer pendingTriggerEventDrainer,
        IRunCompletedPublisher runCompletedPublisher,
        IBookmarkProvider bookmarkProvider,
        ISubWorkflowCompletionHook completionHook,
        IClock clock,
        IRunCancellationService? runCancellationService = null,
        WorkflowMetrics? workflowMetrics = null,
        ILogger<RunFinalizer>? logger = null,
        IJoinAggregatorProvider? joinAggregatorProvider = null)
    {
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _historyEventProvider = historyEventProvider;
        _pendingTriggerEventDrainer = pendingTriggerEventDrainer;
        _runCompletedPublisher = runCompletedPublisher;
        _bookmarkProvider = bookmarkProvider;
        _completionHook = completionHook;
        _clock = clock;
        _workflowMetrics = workflowMetrics;
        _runCancellationService = runCancellationService;
        _joinAggregatorProvider = joinAggregatorProvider;
        _logger = logger;
    }

    public async Task FinalizeAsync(long runId, CancellationToken ct)
    {
        RunRow run = await _runProvider.GetByIdAsync(runId, ct);
        IReadOnlyCollection<BranchRow> branches = await _branchProvider.GetAllByRunIdAsync(run.Id, ct);
        string terminalStatus = DetermineTerminalStatus(run, branches);
        DateTime completedAt = _clock.UtcNow;

        (bool transitioned, RunRow updatedRun) = await _runProvider.SetTerminalAsync(runId, terminalStatus, completedAt, ct);
        if (!transitioned)
        {
            _logger?.LogDebug("Run {RunId} was already terminal ({Status}); skipping finalize side effects.", runId, updatedRun.Status);
            return;
        }

        _workflowMetrics?.RecordRunCompleted(updatedRun.WorkflowRefId.ToString(), terminalStatus);
        await _historyEventProvider.InsertBatchAsync(
        [
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = updatedRun.Id,
                BranchRefId = null,
                NodeId = null,
                EventKind = HistoryEventKind.RunFinalized,
                Severity = GetSeverity(terminalStatus),
                PayloadJson = JsonSerializer.Serialize(new { Status = terminalStatus }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Timestamp = completedAt
            }
        ], ct);
        
        // [RJ]: TODO: investigate what this does
        await _pendingTriggerEventDrainer.DrainAsync(updatedRun.WorkflowRefId, updatedRun.TriggerNodeId, updatedRun.CorrelationKey ?? string.Empty, ct);
        
        // [RJ]: TODO: make this simpler? do we need a wrapper to fire event through the e-bus?
        await _runCompletedPublisher.PublishAsync(updatedRun, terminalStatus, ct);
        await _bookmarkProvider.DeleteAllByRunIdAsync(updatedRun.Id, ct);

        // Join cohorts are kept for the life of the run (they are what you inspect when a cohort
        // looks stuck) and collected here, alongside the bookmarks.
        if (_joinAggregatorProvider is not null)
        {
            await _joinAggregatorProvider.DeleteAllByRunIdAsync(updatedRun.Id, ct);
        }

        await _completionHook.OnRunCompletedAsync(updatedRun.RefId, terminalStatus, ct);
        if (_runCancellationService is not null)
        {
            _runCancellationService.RemoveCts(updatedRun.Id);
        }
    }

    private static string DetermineTerminalStatus(RunRow run, IReadOnlyCollection<BranchRow> branches)
    {
        if (string.Equals(run.Status, "Cancelling", StringComparison.Ordinal))
        {
            if (string.Equals(run.CancellationReason, "OUT_OF_CREDITS", StringComparison.Ordinal))
            {
                return "OutOfCredits";
            }

            // §5.4: a branch that actually completed despite the cancellation request means the
            // run only partially made it out - anything else (nothing completed, whether branches
            // were cancelled, failed, or the request simply beat every branch to the punch) is a
            // clean Cancelled.
            bool hasCancelledBranch = branches.Any(branch => string.Equals(branch.Status, "Cancelled", StringComparison.Ordinal));
            bool hasCompletedDespiteCancellation = branches.Any(branch => string.Equals(branch.Status, "Completed", StringComparison.Ordinal));
            return hasCancelledBranch && hasCompletedDespiteCancellation ? "PartiallyFailed" : "Cancelled";
        }

        if (string.Equals(run.Status, "Failing", StringComparison.Ordinal))
        {
            return "Failed";
        }

        bool hasCompleted = branches.Any(branch => string.Equals(branch.Status, "Completed", StringComparison.Ordinal));
        bool hasFailed = branches.Any(branch => string.Equals(branch.Status, "Failed", StringComparison.Ordinal));

        if (hasFailed && hasCompleted)
        {
            return "PartiallyFailed";
        }

        if (hasFailed)
        {
            return "Failed";
        }

        return "Succeeded";
    }

    private static string GetSeverity(string terminalStatus)
    {
        return terminalStatus switch
        {
            "Failed" or "PartiallyFailed" or "OutOfCredits" => "Warn",
            _ => "Info"
        };
    }
}
