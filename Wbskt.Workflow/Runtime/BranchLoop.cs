using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime.BranchResults;
using Wbskt.Workflow.Telemetry;
using Microsoft.Extensions.Logging;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Runs one branch: loads it, runs its current node, and hands the node's result to the
/// <see cref="IBranchResultHandler"/> for that result's type (through <see cref="IBranchResultDispatcher"/>),
/// until a handler stops the branch. What each result does to the branch - move it, fork it, park it,
/// fail it, end it - lives on its handler, not here.
/// </summary>
internal sealed class BranchLoop : IBranchLoop
{
    private readonly IBranchProvider _branchProvider;
    private readonly IRunProvider _runProvider;
    private readonly IRunCountersProvider _runCountersProvider;
    private readonly IRunJournal _journal;
    private readonly IWorkflowDefinitionCache _workflowDefinitionCache;
    private readonly INodeExecutorRegistry _nodeExecutorRegistry;
    private readonly IIdempotencyKeyProvider _idempotencyKeyProvider;
    private readonly TimeProvider _clock;
    private readonly IRunCancellationService _runCancellationService;
    private readonly ICreditCostCalculator _creditCostCalculator;
    private readonly IBranchResultDispatcher _resultDispatcher;
    private readonly BranchEndings _endings;
    private readonly int _maxBranchLocalStateBytes;
    private readonly WorkflowMetrics? _workflowMetrics;
    private readonly ILogger<BranchLoop>? _logger;

    public BranchLoop(
        IBranchProvider branchProvider,
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IRunJournal journal,
        IWorkflowDefinitionCache workflowDefinitionCache,
        INodeExecutorRegistry nodeExecutorRegistry,
        IIdempotencyKeyProvider idempotencyKeyProvider,
        TimeProvider clock,
        BranchEndings endings,
        IRunCancellationService runCancellationService,
        ICreditCostCalculator creditCostCalculator,
        IBranchResultDispatcher resultDispatcher,
        IOptions<WorkflowEngineOptions>? options = null,
        WorkflowMetrics? workflowMetrics = null,
        ILogger<BranchLoop>? logger = null)
    {
        _branchProvider = branchProvider;
        _runProvider = runProvider;
        _runCountersProvider = runCountersProvider;
        _journal = journal;
        _workflowDefinitionCache = workflowDefinitionCache;
        _nodeExecutorRegistry = nodeExecutorRegistry;
        _idempotencyKeyProvider = idempotencyKeyProvider;
        _clock = clock;
        _runCancellationService = runCancellationService;
        _creditCostCalculator = creditCostCalculator;
        _resultDispatcher = resultDispatcher;
        _endings = endings;
        _maxBranchLocalStateBytes = options?.Value.MaxBranchLocalStateBytes ?? new WorkflowEngineOptions().MaxBranchLocalStateBytes;
        _workflowMetrics = workflowMetrics;
        _logger = logger;
    }

    public async Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken ct)
    {
        CancellationToken runToken = _runCancellationService.GetToken(runId);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, runToken);
        CancellationToken linkedToken = linkedCts.Token;

        BranchRow branchRow = await _branchProvider.GetByIdAsync(branchId, ct);
        RunRow runRow = await _runProvider.GetByIdAsync(runId, ct);
        WorkflowDefinition definition = await _workflowDefinitionCache.GetAsync(runRow.WorkflowDefinitionId, ct);

        // A branch waking from a bookmark is not starting - it is resuming, often hours later. Logging
        // both as "BranchStarted" made a parked-then-woken branch indistinguishable from a fresh one
        // in the trace, and hid how long it had been parked. A branch picked up after a failover is
        // neither: journalling it as a resume made a crash recovery read as a bookmark wake.
        string entryKind = reason switch
        {
            BranchExecutionReason.BookmarkResumed => HistoryEventKind.BranchResumed,
            BranchExecutionReason.Recovered => HistoryEventKind.BranchRecovered,
            _ => HistoryEventKind.BranchStarted
        };
        await _journal.AppendBranchEventAsync(
            runRow.Id,
            branchRow.RefId,
            null,
            entryKind,
            new { reason = reason.ToString() },
            ct);
        _logger?.LogInformation("Started branch loop for run {RunId} branch {BranchId} reason {Reason}", runId, branchId, reason);

        while (true)
        {
            if (await _runCancellationService.IsCancellationRequestedAsync(runId, ct))
            {
                await _endings.CancelAsync(runRow.Id, branchId, branchRow, ct);
                return;
            }

            BaseNode? node = definition.Nodes.SingleOrDefault(candidate => candidate.NodeId == branchRow.NodeId);
            if (node is null)
            {
                await _endings.CompleteAsync(runRow.Id, branchId, branchRow.RefId, ct);
                return;
            }

            BranchContext branchContext = BuildBranchContext(branchRow, runRow, definition.WorkspaceId);

            NodeExecutionResult? result;

            // PendingTakePort is a pre-decided exit: a port recorded on the branch row for the loop to
            // take on re-entry without re-running the node. It is how a branch resumes "already past"
            // its current node - re-executing would repeat the node's side effects.
            if (!string.IsNullOrEmpty(branchRow.PendingTakePort))
            {
                result = new NodeExecutionResult.Continue(branchRow.PendingTakePort, new Dictionary<string, JsonElement>());
            }
            else
            {
                result = await ExecuteNodeAsync(runId, branchId, runRow, branchRow, definition, node, branchContext, linkedToken, ct);
                if (result is null)
                {
                    // Host shutdown interrupted the executor; the branch stays Active for recovery.
                    return;
                }
            }

            if (result is not NodeExecutionResult.Terminal)
            {
                if (runToken.IsCancellationRequested)
                {
                    _logger?.LogInformation("Execution cancelled for node {NodeId} in branch {BranchId} for run {RunId} after executor returned", branchRow.NodeId, branchId, runId);
                    result = new NodeExecutionResult.Terminal(BranchTerminalReason.Cancelled);
                }
                else if (ct.IsCancellationRequested)
                {
                    // Host shutdown fired after the executor returned a non-terminal result. Don't persist it as
                    // Cancelled/Failed - leave the branch Active so recovery re-dispatches it on next startup.
                    _logger?.LogInformation("Host shutdown interrupted branch {BranchId} for run {RunId} after node {NodeId} returned; leaving branch Active for recovery.", branchId, runId, branchRow.NodeId);
                    return;
                }
            }

            BranchResultContext resultContext = new(runId, branchId, runRow, branchRow, definition, node, branchContext);
            BranchResultOutcome outcome = await _resultDispatcher.DispatchAsync(result, resultContext, ct);
            if (outcome.Branch is not { } advanced)
            {
                return;
            }

            branchRow = advanced;
        }
    }

    /// <summary>
    /// Runs <paramref name="node"/> through its executor (with retries) and records its outcome. A
    /// failure, a crash or a run cancellation comes back as a result; null means host shutdown
    /// interrupted the executor and the branch must be left Active, untouched, for recovery.
    /// </summary>
    private async Task<NodeExecutionResult?> ExecuteNodeAsync(
        long runId,
        long branchId,
        RunRow runRow,
        BranchRow branchRow,
        WorkflowDefinition definition,
        BaseNode node,
        BranchContext branchContext,
        CancellationToken linkedToken,
        CancellationToken ct)
    {
        NodeExecutionResult? result = null;
        await _journal.AppendBranchEventAsync(runRow.Id, branchRow.RefId, node.NodeId, HistoryEventKind.NodeStarted, ct);
        _logger?.LogInformation("Executing node {NodeId} ({NodeKind}) on run {RunId} branch {BranchId}", node.NodeId, node.Kind, runId, branchId);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string outcome = "Succeeded";
        bool hostShutdownInterrupted = false;
        // Accumulated across attempts, so a node retried three times reports what all three cost.
        decimal creditsCharged = 0m;

        // The resumer and the timer leave a parked branch Waiting at its node and only dispatch it; the
        // node flips it back to Active when it moves on. So a branch that arrives here Waiting is on the
        // resume visit of a node it already paid to park on.
        bool isResumeVisit = string.Equals(branchRow.Status, BranchStatus.Waiting, StringComparison.Ordinal);
        try
        {
            // Resolving the executor MUST stay inside this try. An unregistered node kind throws,
            // and if that escapes RunAsync the branch is left Active forever: the finalizer never
            // runs (ActiveBranchCount is never decremented), Run_GetStuck deliberately skips runs
            // that still have Active branches so the reaper won't collect it, and RunRecoveryService
            // re-dispatches it into the same crash on every restart.
            INodeExecutor executor = _nodeExecutorRegistry.For(node.Kind);

            result = await RetryExecutor.RunWithRetryAsync(
                node,
                branchContext,
                executor,
                new NodeExecutionServices(_idempotencyKeyProvider),
                _clock,
                linkedToken,
                _runCountersProvider,
                _creditCostCalculator,
                workflowMetrics: _workflowMetrics,
                definition: definition,
                onRetry: (attempt, maxAttempts, delay, reason, retryCt) => _journal.AppendBranchEventAsync(
                    runRow.Id,
                    branchRow.RefId,
                    node.NodeId,
                    HistoryEventKind.NodeRetrying,
                    new { attempt, maxAttempts, delayMs = delay.TotalMilliseconds, reason },
                    retryCt),
                // Summed rather than written per attempt: a row per charge would roughly double
                // history volume for a number that belongs on the outcome event anyway.
                onCharge: cost => creditsCharged += cost,
                isResumeVisit: isResumeVisit);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && await _runCancellationService.IsCancellationRequestedAsync(runId, CancellationToken.None))
            {
                _logger?.LogInformation("Execution cancelled for node {NodeId} in branch {BranchId} for run {RunId}", branchRow.NodeId, branchId, runId);
                outcome = "Cancelled";
                result = new NodeExecutionResult.Terminal(BranchTerminalReason.Cancelled);
            }
            else if (ex is OperationCanceledException && ct.IsCancellationRequested)
            {
                // Host shutdown (not a run-level cancellation) interrupted the executor. Leave the branch
                // Active and untouched - RunRecoveryService re-dispatches it on the next startup (see 1.1).
                outcome = "Interrupted";
                hostShutdownInterrupted = true;
                _logger?.LogInformation("Host shutdown interrupted node {NodeId} in branch {BranchId} for run {RunId}; leaving branch Active for recovery.", branchRow.NodeId, branchId, runId);
            }
            else if (ex is NodeKindNotSupportedException)
            {
                _logger?.LogError(ex, "Node {NodeId} in branch {BranchId} for run {RunId} has unsupported kind {NodeKind}", branchRow.NodeId, branchId, runId, node.Kind);
                outcome = "Failed";
                result = new NodeExecutionResult.Fail("NODE_KIND_NOT_SUPPORTED", ex.Message, false, ex);
            }
            else
            {
                _logger?.LogError(ex, "Exception occurred executing node {NodeId} in branch {BranchId} for run {RunId}", branchRow.NodeId, branchId, runId);
                outcome = "Failed";
                result = new NodeExecutionResult.Fail("EXECUTOR_CRASH", ex.Message, false, ex);
            }
        }
        finally
        {
            stopwatch.Stop();
            if (outcome == "Succeeded" && result is NodeExecutionResult.Fail)
            {
                outcome = "Failed";
            }
            _workflowMetrics?.RecordNodeDuration(node.Kind, outcome, stopwatch.Elapsed.TotalMilliseconds);
        }

        if (hostShutdownInterrupted || result is null)
        {
            return null;
        }

        result = GuardLocalStateSize(result, branchRow);

        // durationMs travels on every node outcome. Deriving it by subtracting the NodeStarted
        // timestamp only works while both rows survive retention, and it silently includes any
        // retry backoff; this is the executor's own elapsed time and is what per-node timing
        // analytics reads.
        double durationMs = stopwatch.Elapsed.TotalMilliseconds;
        object eventPayload = result switch
        {
            NodeExecutionResult.Fail fail => new { fail.ErrorCode, fail.Message, durationMs, creditsCharged },
            NodeExecutionResult.Continue cont => new { port = cont.OutboundPort, output = cont.LocalStatePatch, durationMs, creditsCharged },
            _ => new { durationMs, creditsCharged }
        };

        _logger?.LogInformation("Node {NodeId} ({NodeKind}) completed with outcome {Outcome} on run {RunId} branch {BranchId}", node.NodeId, node.Kind, result.GetType().Name, runId, branchId);
        await _journal.AppendBranchEventAsync(runRow.Id, branchRow.RefId, node.NodeId, result is NodeExecutionResult.Fail ? HistoryEventKind.NodeFailed : HistoryEventKind.NodeCompleted, eventPayload, ct);

        return result;
    }

    /// <summary>
    /// Fails the node with BRANCH_STATE_TOO_LARGE when the local state its result would leave on the
    /// branch is over <see cref="WorkflowEngineOptions.MaxBranchLocalStateBytes"/>. Every node output
    /// is merged into the branch row and the whole of it is read back, deserialized and written again
    /// on every later step, so one oversized payload made each step after it slower and every row it
    /// was copied into (a fork child, a join continuation) larger.
    /// </summary>
    private NodeExecutionResult GuardLocalStateSize(NodeExecutionResult result, BranchRow branchRow)
    {
        long bytes = result switch
        {
            NodeExecutionResult.Continue cont => SizeOf(BranchLocalState.Merge(branchRow.LocalJson, cont.LocalStatePatch, cont.RemoveKeys)),
            NodeExecutionResult.WaitForBookmark wait => SizeOf(BranchLocalState.Merge(branchRow.LocalJson, wait.LocalStatePatch)),
            // Each child gets the shared state plus its own; base plus the largest child is an upper
            // bound on any child's row without serializing the shared state once per child.
            NodeExecutionResult.Fork fork => SizeOf(BranchLocalState.Merge(branchRow.LocalJson, fork.LocalStatePatch))
                + (fork.Children.Count == 0 ? 0 : fork.Children.Max(child => SizeOf(child.LocalState))),
            _ => 0
        };

        if (bytes <= _maxBranchLocalStateBytes)
        {
            return result;
        }

        return new NodeExecutionResult.Fail(
            "BRANCH_STATE_TOO_LARGE",
            $"The node's output would grow the branch's local state to {bytes} bytes, over the limit of {_maxBranchLocalStateBytes} bytes.",
            false,
            null);
    }

    private static long SizeOf(IReadOnlyDictionary<string, JsonElement> localState)
    {
        return System.Text.Encoding.UTF8.GetByteCount(BranchLocalState.Serialize(localState));
    }

    private static BranchContext BuildBranchContext(BranchRow branchRow, RunRow runRow, int workspaceId)
    {
        IReadOnlyDictionary<string, JsonElement> localState = BranchLocalState.Deserialize(branchRow.LocalJson);

        IReadOnlyDictionary<string, JsonElement> triggerPayload;
        if (localState.TryGetValue("trigger", out JsonElement triggerElement)
            && triggerElement.ValueKind == JsonValueKind.Object)
        {
            triggerPayload = triggerElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }
        else
        {
            triggerPayload = new Dictionary<string, JsonElement>();
        }

        return new BranchContext(
            runRow.Id,
            branchRow.Id,
            runRow.WorkflowDefinitionId,
            runRow.WorkflowRefId,
            runRow.WorkflowVersion,
            branchRow.NodeId.ToString(),
            1,
            localState,
            triggerPayload,
            runRow.CorrelationKey ?? string.Empty,
            runRow.StartedAt,
            workspaceId)
        {
            RunRefId = runRow.RefId,
            BranchRefId = branchRow.RefId,
            VisitToken = Convert.ToHexString(branchRow.RowVersion)
        };
    }
}
