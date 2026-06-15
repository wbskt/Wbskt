using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunFinalizer : IRunFinalizer
{
    private readonly IRunProvider _runProvider;
    private readonly IRunCountersProvider _runCountersProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IHistoryEventProvider _historyEventProvider;
    private readonly IPendingTriggerEventDrainer _pendingTriggerEventDrainer;
    private readonly IRunCompletedPublisher _runCompletedPublisher;
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly ISubWorkflowCompletionHook _completionHook;
    private readonly IClock _clock;
    private readonly IRunCancellationService? _runCancellationService;

    public RunFinalizer(
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBranchProvider branchProvider,
        IHistoryEventProvider historyEventProvider,
        IPendingTriggerEventDrainer pendingTriggerEventDrainer,
        IRunCompletedPublisher runCompletedPublisher,
        IBookmarkProvider bookmarkProvider,
        ISubWorkflowCompletionHook completionHook,
        IClock clock,
        IRunCancellationService? runCancellationService = null)
    {
        _runProvider = runProvider;
        _runCountersProvider = runCountersProvider;
        _branchProvider = branchProvider;
        _historyEventProvider = historyEventProvider;
        _pendingTriggerEventDrainer = pendingTriggerEventDrainer;
        _runCompletedPublisher = runCompletedPublisher;
        _bookmarkProvider = bookmarkProvider;
        _completionHook = completionHook;
        _clock = clock;
        _runCancellationService = runCancellationService;
    }

    public async Task FinalizeAsync(long runId, CancellationToken ct)
    {
        RunRow run = await _runProvider.GetByIdAsync(runId, ct);
        RunCountersRow counters = await _runCountersProvider.GetByRunIdAsync(run.Id, ct);
        _ = counters;
        IReadOnlyCollection<BranchRow> branches = await _branchProvider.GetAllByRunIdAsync(run.Id, ct);
        string terminalStatus = DetermineTerminalStatus(run.Status, branches);
        DateTime completedAt = _clock.UtcNow;

        RunRow updatedRun = await _runProvider.SetTerminalAsync(runId, terminalStatus, completedAt, ct);
        await _historyEventProvider.InsertBatchAsync(
        [
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = updatedRun.Id,
                BranchRefId = null,
                NodeId = null,
                EventKind = "RunFinalized",
                Severity = GetSeverity(terminalStatus),
                PayloadJson = JsonSerializer.Serialize(new { Status = terminalStatus }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Timestamp = completedAt
            }
        ], ct);
        await _pendingTriggerEventDrainer.DrainAsync(updatedRun.WorkflowRefId, updatedRun.TriggerNodeId, updatedRun.CorrelationKey ?? string.Empty, ct);
        await _runCompletedPublisher.PublishAsync(updatedRun, terminalStatus, ct);
        await _bookmarkProvider.DeleteAllByRunIdAsync(updatedRun.Id, ct);
        await _completionHook.OnRunCompletedAsync(updatedRun.RefId, terminalStatus, ct);
        if (_runCancellationService is not null)
        {
            _runCancellationService.RemoveCts(updatedRun.Id);
        }
    }

    private static string DetermineTerminalStatus(string currentStatus, IReadOnlyCollection<BranchRow> branches)
    {
        if (string.Equals(currentStatus, "Cancelling", StringComparison.Ordinal))
        {
            return "Cancelled";
        }

        if (string.Equals(currentStatus, "Failing", StringComparison.Ordinal))
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
            "Failed" or "PartiallyFailed" => "Warn",
            _ => "Info"
        };
    }
}
