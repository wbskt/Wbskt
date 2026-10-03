using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Telemetry;
using Microsoft.Extensions.Logging;

namespace Wbskt.Workflow.Runtime;

internal sealed class BranchLoop : IBranchLoop
{
    private const string ActiveStatus = "Active";
    private const string CompletedStatus = "Completed";

    /// <summary>
    /// Local-state key marking a branch as a member of a fan-out cohort (written by ForkNodeExecutor
    /// and ParallelForEachNodeExecutor, read by JoinNodeExecutor). Kept here too because the fail
    /// path must contribute on behalf of a branch that will never reach the Join node.
    /// </summary>
    internal const string JoinTokenKey = "__join_token";
    private readonly IBranchProvider _branchProvider;
    private readonly IRunProvider _runProvider;
    private readonly IRunCountersProvider _runCountersProvider;
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IHistoryEventProvider _historyEventProvider;
    private readonly IWorkflowDefinitionCache _workflowDefinitionCache;
    private readonly INodeExecutorRegistry _nodeExecutorRegistry;
    private readonly IRunDispatcher _runDispatcher;
    private readonly IProviderComposite _providerComposite;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly IRunFinalizer? _runFinalizer;
    private readonly IRunCancellationService _runCancellationService;
    private readonly ICompensationOrchestrator? _compensationOrchestrator;
    private readonly ICreditCostCalculator _creditCostCalculator;
    private readonly IJoinAggregatorProvider? _joinAggregatorProvider;
    private readonly WorkflowMetrics? _workflowMetrics;
    private readonly ILogger<BranchLoop>? _logger;
    private readonly OnFailureHandler _onFailureHandler = new();

    public BranchLoop(
        IBranchProvider branchProvider,
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBookmarkProvider bookmarkProvider,
        IHistoryEventProvider historyEventProvider,
        IWorkflowDefinitionCache workflowDefinitionCache,
        INodeExecutorRegistry nodeExecutorRegistry,
        IRunDispatcher runDispatcher,
        IProviderComposite providerComposite,
        IClock clock,
        IIdGenerator idGenerator,
        IRunFinalizer? runFinalizer = null,
        IRunCancellationService? runCancellationService = null,
        ICompensationOrchestrator? compensationOrchestrator = null,
        ICreditCostCalculator? creditCostCalculator = null,
        WorkflowMetrics? workflowMetrics = null,
        ILogger<BranchLoop>? logger = null,
        IJoinAggregatorProvider? joinAggregatorProvider = null)
    {
        _branchProvider = branchProvider;
        _runProvider = runProvider;
        _runCountersProvider = runCountersProvider;
        _bookmarkProvider = bookmarkProvider;
        _historyEventProvider = historyEventProvider;
        _workflowDefinitionCache = workflowDefinitionCache;
        _nodeExecutorRegistry = nodeExecutorRegistry;
        _runDispatcher = runDispatcher;
        _providerComposite = providerComposite;
        _clock = clock;
        _idGenerator = idGenerator;
        _workflowMetrics = workflowMetrics;
        _runFinalizer = runFinalizer;
        _runCancellationService = runCancellationService ?? new NoOpRunCancellationService();
        _compensationOrchestrator = compensationOrchestrator;
        _creditCostCalculator = creditCostCalculator ?? new DefaultCreditCostCalculator();
        _joinAggregatorProvider = joinAggregatorProvider;
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
        // in the trace, and hid how long it had been parked.
        string entryKind = reason == BranchExecutionReason.BookmarkResumed
            ? HistoryEventKind.BranchResumed
            : HistoryEventKind.BranchStarted;
        await AppendEventAsync(
            runRow.Id,
            branchRow.RefId,
            null,
            entryKind,
            JsonSerializer.Serialize(new { reason = reason.ToString() }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ct);
        _logger?.LogInformation("Started branch loop for run {RunId} branch {BranchId} reason {Reason}", runId, branchId, reason);

        // Spec §2.10 Branch Loop (pseudocode)
        // loop:
        //   node ← lookup(definition, branch.CurrentNodeId)
        //   if not found → branch ends (Completed, no edges)
        //   emit HistoryEvent.NodeStarted
        //   executor ← registry[node.Kind]
        //   ctx      ← buildContext(run, branch)
        //   result   ← executor.ExecuteAsync(node, ctx)
        //   switch result:
        //     Continue: resolve next node, persist pointer, loop
        //     Fork: handled in later phase task
        //     WaitForBookmark: handled in later phase task
        //     Fail: handled in later phase task
        //     Terminal: branch ends (Completed)
        while (true)
        {
            if (await _runCancellationService.IsCancellationRequestedAsync(runId, ct))
            {
                await CancelBranchAsync(runRow.Id, branchId, branchRow, ct);
                return;
            }

            BaseNode? node = definition.Nodes.SingleOrDefault(candidate => candidate.NodeId == branchRow.NodeId);
            if (node is null)
            {
                await CompleteBranchAsync(runRow.Id, branchId, branchRow.RefId, ct);
                return;
            }

            BranchContext branchContext = BuildBranchContext(branchRow, runRow, definition.WorkspaceId);

            NodeExecutionResult? result = null;
            
            // PendingTakePort is a pre-decided exit: a port recorded on the branch row for the loop to
            // take on re-entry without re-running the node. It is how a branch resumes "already past"
            // its current node - re-executing would repeat the node's side effects.
            if (!string.IsNullOrEmpty(branchRow.PendingTakePort))
            {
                result = new NodeExecutionResult.Continue(branchRow.PendingTakePort, new Dictionary<string, JsonElement>());
            }
            else
            {
                await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, HistoryEventKind.NodeStarted, ct);
                _logger?.LogInformation("Executing node {NodeId} ({NodeKind}) on run {RunId} branch {BranchId}", node.NodeId, node.Kind, runId, branchId);

                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                string outcome = "Succeeded";
                bool hostShutdownInterrupted = false;
                // Accumulated across attempts, so a node retried three times reports what all three cost.
                decimal creditsCharged = 0m;
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
                        new NodeExecutionServices(_providerComposite),
                        _clock,
                        linkedToken,
                        _runCountersProvider,
                        _creditCostCalculator,
                        workflowMetrics: _workflowMetrics,
                        definition: definition,
                        onRetry: (attempt, maxAttempts, delay, reason, retryCt) => AppendEventAsync(
                            runRow.Id,
                            branchRow.RefId,
                            node.NodeId,
                            HistoryEventKind.NodeRetrying,
                            JsonSerializer.Serialize(
                                new { attempt, maxAttempts, delayMs = delay.TotalMilliseconds, reason },
                                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                            retryCt),
                        // Summed rather than written per attempt: a row per charge would roughly double
                        // history volume for a number that belongs on the outcome event anyway.
                        onCharge: cost => creditsCharged += cost);
                }
                catch (EngineFaultException ex)
                {
                    _logger?.LogError(ex, "Engine fault occurred executing node {NodeId} in branch {BranchId} for run {RunId}", branchRow.NodeId, branchId, runId);
                    outcome = "Failed";
                    await _runProvider.TransitionStatusAsync(runRow.Id, runRow.Status, "Faulted", null, null, ct);
                    string faultJson = JsonSerializer.Serialize(new { ex.Message }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, HistoryEventKind.RunFaulted, faultJson, ct);
                    // Faulted is terminal for the run (the Run_SetTerminal guard now protects it from being
                    // overwritten by sibling completions), so drain this branch's slot directly instead of
                    // going through the finalizer.
                    await _branchProvider.SetFailedAsync(branchId, faultJson, ct);
                    await _runCountersProvider.DecrementActiveBranchesAsync(runRow.Id, 1, ct);
                    throw;
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
                    return;
                }

                // durationMs travels on every node outcome. Deriving it by subtracting the NodeStarted
                // timestamp only works while both rows survive retention, and it silently includes any
                // retry backoff; this is the executor's own elapsed time and is what per-node timing
                // analytics reads.
                double durationMs = stopwatch.Elapsed.TotalMilliseconds;
                string? eventPayload = result switch
                {
                    NodeExecutionResult.Fail fail => JsonSerializer.Serialize(new { fail.ErrorCode, fail.Message, durationMs, creditsCharged }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    NodeExecutionResult.Continue cont => JsonSerializer.Serialize(new { port = cont.OutboundPort, output = cont.LocalStatePatch, durationMs, creditsCharged }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    _ => JsonSerializer.Serialize(new { durationMs, creditsCharged }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                };

                _logger?.LogInformation("Node {NodeId} ({NodeKind}) completed with outcome {Outcome} on run {RunId} branch {BranchId}", node.NodeId, node.Kind, result.GetType().Name, runId, branchId);
                await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, result is NodeExecutionResult.Fail ? HistoryEventKind.NodeFailed : HistoryEventKind.NodeCompleted, eventPayload, ct);
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

            switch (result)
            {
                case NodeExecutionResult.Continue @continue:
                {
                    Guid? nextNodeId = ResolveNextNodeId(definition, node.NodeId, @continue.OutboundPort);
                    if (nextNodeId is null)
                    {
                        await CompleteBranchAsync(runRow.Id, branchId, branchRow.RefId, ct);
                        return;
                    }

                    string localJson = SerializeLocalState(MergeLocalState(branchRow.LocalJson, @continue.LocalStatePatch, @continue.RemoveKeys));
                    branchRow = await _branchProvider.UpdatePointerAsync(branchId, nextNodeId.Value, ActiveStatus, localJson, branchRow.LastOutputJson, ct);
                    break;
                }

                case NodeExecutionResult.Fork fork:
                {
                    IReadOnlyDictionary<string, JsonElement> baseLocalState = MergeLocalState(branchRow.LocalJson, fork.LocalStatePatch);
                    if (fork.Children.Count == 1 && string.IsNullOrWhiteSpace(fork.ContinueOutboundPort))
                    {
                        ForkSpec child = fork.Children.Single();
                        string childLocalJson = SerializeLocalState(MergeLocalState(SerializeLocalState(baseLocalState), child.LocalState));
                        branchRow = await _branchProvider.UpdatePointerAsync(branchId, ResolveForkTargetNodeId(definition, node.NodeId, child.OutboundPort), ActiveStatus, childLocalJson, branchRow.LastOutputJson, ct);
                        break;
                    }

                    if (fork.Children.Count > 0)
                    {
                        await _runCountersProvider.IncrementActiveBranchesAsync(runRow.Id, fork.Children.Count, ct);
                        Guid cohortId = _idGenerator.NewId();
                        foreach (ForkSpec child in fork.Children)
                        {
                            string childLocalJson = SerializeLocalState(MergeLocalState(SerializeLocalState(baseLocalState), child.LocalState));
                            BranchRow created = await _branchProvider.CreateAsync(new BranchRow
                            {
                                Id = 0,
                                RefId = _idGenerator.NewId(),
                                RunId = runRow.Id,
                                ParentBranchId = branchRow.RefId,
                                ForkCohortId = cohortId,
                                NodeId = ResolveForkTargetNodeId(definition, node.NodeId, child.OutboundPort),
                                Status = ActiveStatus,
                                PendingTakePort = null,
                                LocalJson = childLocalJson,
                                LastOutputJson = null,
                                CompensationStackJson = branchRow.CompensationStackJson,
                                CreatedAt = _clock.UtcNow,
                                UpdatedAt = _clock.UtcNow,
                                RowVersion = Array.Empty<byte>()
                            }, ct);
                            await _runDispatcher.DispatchAsync(new BranchExecutionRequest(runId, created.Id, BranchExecutionReason.ForkChild), ct);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(fork.ContinueOutboundPort))
                    {
                        await CompleteBranchAsync(runRow.Id, branchId, branchRow.RefId, ct);
                        return;
                    }

                    string continueLocalJson = SerializeLocalState(baseLocalState);
                    branchRow = await _branchProvider.UpdatePointerAsync(branchId, ResolveForkTargetNodeId(definition, node.NodeId, fork.ContinueOutboundPort), ActiveStatus, continueLocalJson, branchRow.LastOutputJson, ct);
                    break;
                }

                case NodeExecutionResult.WaitForBookmark wait:
                {
                    string localJson = SerializeLocalState(MergeLocalState(branchRow.LocalJson, wait.LocalStatePatch));
                    branchRow = await _branchProvider.UpdatePointerAsync(branchId, branchRow.NodeId, "Waiting", localJson, branchRow.LastOutputJson, ct);
                    await AppendEventAsync(runRow.Id, branchRow.RefId, branchRow.NodeId, HistoryEventKind.BranchParked, ct);

                    // Single-row model (design §3.5): one bookmark row per wait. A TTL, if present,
                    // lives on the same row (ExpiresAt/TtlPort) rather than a separate companion timer
                    // row - whichever side (signal/http/child match vs. TTL expiry) claims the row
                    // first wins; the loser's claim is a no-op.
                    DateTime nowUtc = _clock.UtcNow;
                    await _bookmarkProvider.CreateAsync(CreateBookmarkRow(runRow.Id, branchRow.RefId, branchRow.NodeId, wait.Condition, nowUtc), ct);

                    return;
                }

                case NodeExecutionResult.Fail fail:
                {
                    NodeExecutionResult handledResult = _onFailureHandler.Apply(fail, node, branchContext);
                    if (handledResult is NodeExecutionResult.Continue handledContinue)
                    {
                        Guid? nextNodeId = ResolveNextNodeId(definition, node.NodeId, handledContinue.OutboundPort);
                        if (nextNodeId is null)
                        {
                            await CompleteBranchAsync(runRow.Id, branchId, branchRow.RefId, ct);
                            return;
                        }

                        string localJson = SerializeLocalState(MergeLocalState(branchRow.LocalJson, handledContinue.LocalStatePatch, handledContinue.RemoveKeys));
                        branchRow = await _branchProvider.UpdatePointerAsync(branchId, nextNodeId.Value, ActiveStatus, localJson, branchRow.LastOutputJson, ct);
                        break;
                    }

                    string errorJson = JsonSerializer.Serialize(new { fail.ErrorCode, fail.Message }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    OnFailureConfig? config = (node as BaseActionNode)?.OnFailure;
                    ErrorOutcome outcome = config?.Outcome ?? ErrorOutcome.FailBranch;
                    bool shouldCompensate = (outcome == ErrorOutcome.Compensate) || (definition.RunCompensationOnFailure);

                    if (outcome == ErrorOutcome.FailRun)
                    {
                        await _branchProvider.SetFailedAsync(branchId, errorJson, ct);
                        await _runProvider.TransitionStatusAsync(runRow.Id, "Running", "Failing", null, null, ct);
                        await _runCancellationService.RequestCancellationAsync(runRow.Id, $"FailRun cascade triggered by node {node.NodeId}", ct);
                    }
                    else if (shouldCompensate)
                    {
                        branchRow = await _branchProvider.UpdatePointerAsync(branchId, node.NodeId, "Compensating", branchRow.LocalJson, branchRow.LastOutputJson, ct);
                        if (_compensationOrchestrator is not null)
                        {
                            await _compensationOrchestrator.RunAsync(runRow.Id, branchId, ct);
                        }
                        await _branchProvider.SetFailedAsync(branchId, errorJson, ct);
                    }
                    else
                    {
                        await _branchProvider.SetFailedAsync(branchId, errorJson, ct);
                        if (definition.FailFast)
                        {
                            await _runProvider.TransitionStatusAsync(runRow.Id, "Running", "Failing", null, null, ct);
                        }
                    }

                    if (string.Equals(fail.ErrorCode, "OUT_OF_CREDITS", StringComparison.Ordinal))
                    {
                        // Credit exhaustion is a run-wide constraint - cascade cancellation so sibling
                        // branches stop instead of continuing to burn (already-exhausted) budget.
                        await _runCancellationService.RequestCancellationAsync(runRow.Id, "OUT_OF_CREDITS", ct);
                    }

                    // The branch is over. Only NodeFailed was recorded before, so a reader could see
                    // which node failed but not that the branch itself ended, nor how it was handled -
                    // FailBranch, FailRun and Compensate look identical from NodeFailed alone.
                    await AppendEventAsync(
                        runRow.Id,
                        branchRow.RefId,
                        node.NodeId,
                        HistoryEventKind.BranchFailed,
                        JsonSerializer.Serialize(
                            new { fail.ErrorCode, fail.Message, onFailure = outcome.ToString(), compensated = shouldCompensate },
                            new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                        ct);

                    // This branch is dying, so it will never reach its Join. Contribute on its behalf,
                    // otherwise a Mode=All cohort never reaches ExpectedCount and everything after the
                    // Join is silently skipped until the reaper eventually cancels the run.
                    await ContributeFailureToJoinAsync(runRow, branchRow, definition, ct);

                    int postDecrementCount = await _runCountersProvider.DecrementActiveBranchesAsync(runRow.Id, 1, ct);
                    if (postDecrementCount == 0 && _runFinalizer is not null)
                    {
                        await _runFinalizer.FinalizeAsync(runRow.Id, ct);
                    }

                    return;
                }

                case NodeExecutionResult.Terminal terminal when terminal.Reason == BranchTerminalReason.Cancelled:
                    await CancelBranchAsync(runRow.Id, branchId, branchRow, ct);
                    return;

                case NodeExecutionResult.Terminal:
                    await CompleteBranchAsync(runRow.Id, branchId, branchRow.RefId, ct);
                    return;

                default:
                    throw new NotImplementedException("Additional branch loop outcomes are implemented in later phase tasks.");
            }
        }
    }

    /// <summary>
    /// Records a dying branch's "failed" arrival at the Join its cohort converges on, and — if that
    /// arrival is the one that satisfies the quorum — spawns the continuation branch the Join would
    /// otherwise have continued on.
    ///
    /// A branch that fails never reaches the Join node, so without this a Mode=All cohort can never
    /// reach ExpectedCount: the aggregator stays unclaimed, everything downstream of the Join is
    /// silently skipped, and the run sits Running until the reaper collects it.
    ///
    /// The continuation is a fresh branch rather than a resurrection of the failed one: the failed
    /// branch keeps its Failed status (so the run still finalizes as Failed/PartiallyFailed), while
    /// the work after the Join proceeds. Best-effort — a failure here must not mask the original node
    /// failure that brought us into this path.
    /// </summary>
    private async Task ContributeFailureToJoinAsync(RunRow runRow, BranchRow branchRow, WorkflowDefinition definition, CancellationToken ct)
    {
        if (_joinAggregatorProvider is null)
        {
            return;
        }

        IReadOnlyDictionary<string, JsonElement> localState = DeserializeDictionary(branchRow.LocalJson);
        if (!localState.TryGetValue(JoinTokenKey, out JsonElement tokenElement)
            || tokenElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(tokenElement.GetString(), out Guid joinToken))
        {
            // Not part of a Fork/ParallelForEach cohort - nothing to contribute to.
            return;
        }

        try
        {
            JoinContributionResult contribution = await _joinAggregatorProvider.ContributeAsync(joinToken, branchRow.Id, "failed", ct);
            _logger?.LogInformation(
                "Branch {BranchId} failed and contributed 'failed' to join {JoinToken} ({Contributed}/{Expected}, {Failed} failed).",
                branchRow.Id, joinToken, contribution.ContributedCount, contribution.ExpectedCount, contribution.FailedCount);

            if (!contribution.ShouldContinue || contribution.JoinNodeId is not Guid joinNodeId)
            {
                return;
            }

            Guid? continuationNodeId = WorkflowGraph.ResolveTarget(definition, joinNodeId, "default");
            if (continuationNodeId is null)
            {
                // The Join's "default" port leads nowhere, so there is nothing after it to run.
                return;
            }

            string continuationLocalJson = SerializeLocalState(new Dictionary<string, JsonElement>
            {
                ["joinContributed"] = JsonSerializer.SerializeToElement(contribution.ContributedCount),
                ["joinSucceeded"] = JsonSerializer.SerializeToElement(contribution.SucceededCount),
                ["joinFailed"] = JsonSerializer.SerializeToElement(contribution.FailedCount)
            });

            // Count the continuation BEFORE creating it, so the caller's own decrement cannot drive
            // the run to zero active branches and finalize it out from under the new branch.
            await _runCountersProvider.IncrementActiveBranchesAsync(runRow.Id, 1, ct);

            BranchRow continuation = await _branchProvider.CreateAsync(new BranchRow
            {
                Id = 0,
                RefId = _idGenerator.NewId(),
                RunId = runRow.Id,
                ParentBranchId = branchRow.RefId,
                ForkCohortId = branchRow.ForkCohortId,
                NodeId = continuationNodeId.Value,
                Status = ActiveStatus,
                PendingTakePort = null,
                LocalJson = continuationLocalJson,
                LastOutputJson = null,
                CompensationStackJson = branchRow.CompensationStackJson,
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow,
                RowVersion = Array.Empty<byte>()
            }, ct);

            await AppendEventAsync(runRow.Id, continuation.RefId, joinNodeId, HistoryEventKind.JoinContinuationSpawned, ct);
            await _runDispatcher.DispatchAsync(new BranchExecutionRequest(runRow.Id, continuation.Id, BranchExecutionReason.ForkChild), ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to contribute branch {BranchId}'s failure to its join cohort on run {RunId}.", branchRow.Id, runRow.Id);
        }
    }

    private async Task CompleteBranchAsync(int runId, long branchId, Guid branchRefId, CancellationToken ct)
    {
        await _branchProvider.SetCompletedAsync(branchId, ct);
        int postDecrementCount = await _runCountersProvider.IncrementActiveBranchesAsync(runId, -1, ct);
        await AppendEventAsync(runId, branchRefId, null, HistoryEventKind.BranchCompleted, ct);

        if (postDecrementCount == 0 && _runFinalizer is not null)
        {
            await _runFinalizer.FinalizeAsync(runId, ct);
        }
    }

    private async Task CancelBranchAsync(int runId, long branchId, BranchRow branchRow, CancellationToken ct)
    {
        await _branchProvider.UpdatePointerAsync(branchId, branchRow.NodeId, "Cancelled", branchRow.LocalJson, branchRow.LastOutputJson, ct);
        int postDecrementCount = await _runCountersProvider.IncrementActiveBranchesAsync(runId, -1, ct);
        await AppendEventAsync(runId, branchRow.RefId, null, HistoryEventKind.BranchCancelled, ct);

        if (postDecrementCount == 0 && _runFinalizer is not null)
        {
            await _runFinalizer.FinalizeAsync(runId, ct);
        }
    }

    private Task AppendEventAsync(int runId, Guid branchRefId, Guid? nodeId, string eventKind, CancellationToken ct)
    {
        return AppendEventAsync(runId, branchRefId, nodeId, eventKind, null, ct);
    }

    private async Task AppendEventAsync(int runId, Guid branchRefId, Guid? nodeId, string eventKind, string? payloadJson, CancellationToken ct)
    {
        await _historyEventProvider.InsertBatchAsync([
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = runId,
                BranchRefId = branchRefId,
                NodeId = nodeId,
                EventKind = eventKind,
                Severity = HistoryEventKind.SeverityFor(eventKind),
                PayloadJson = payloadJson,
                Timestamp = _clock.UtcNow
            }
        ], ct);
    }

    private static BranchContext BuildBranchContext(BranchRow branchRow, RunRow runRow, int workspaceId)
    {
        IReadOnlyDictionary<string, JsonElement> localState = DeserializeDictionary(branchRow.LocalJson);

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

    private static IReadOnlyDictionary<string, JsonElement> MergeLocalState(
        string existingJson,
        IReadOnlyDictionary<string, JsonElement> patch,
        IReadOnlyCollection<string>? removeKeys = null)
    {
        Dictionary<string, JsonElement> merged = new(DeserializeDictionary(existingJson), StringComparer.Ordinal);

        // Removals run first so a node can drop a key and re-add it in the same step.
        if (removeKeys is not null)
        {
            foreach (string key in removeKeys)
            {
                merged.Remove(key);
            }
        }

        foreach (var pair in patch)
        {
            merged[pair.Key] = pair.Value.Clone();
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, JsonElement> DeserializeDictionary(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? new Dictionary<string, JsonElement>();
    }

    private string SerializeLocalState(IReadOnlyDictionary<string, JsonElement> localState)
    {
        return JsonSerializer.Serialize(localState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private BookmarkRow CreateBookmarkRow(int runId, Guid branchRefId, Guid nodeId, WakeCondition condition, DateTime nowUtc)
    {
        return new BookmarkRow
        {
            Id = 0,
            RefId = _idGenerator.NewId(),
            RunId = runId,
            BranchRefId = branchRefId,
            NodeId = nodeId,
            WakeConditionKind = GetWakeConditionKind(condition),
            MatchKey = GetWakeConditionMatchKey(condition),
            WakeConditionJson = JsonSerializer.Serialize(condition, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExpiresAt = GetWakeConditionExpiresAt(condition, nowUtc),
            TtlPort = condition.TtlPort,
            CreatedAt = nowUtc
        };
    }

    private static DateTime? GetWakeConditionExpiresAt(WakeCondition condition, DateTime nowUtc)
    {
        return condition switch
        {
            TimerWakeCondition timer => timer.At,
            _ => condition.Ttl is TimeSpan ttl ? nowUtc + ttl : null
        };
    }

    private static string GetWakeConditionKind(WakeCondition condition)
    {
        return condition switch
        {
            TimerWakeCondition => "timer",
            SignalWakeCondition => "signal",
            InboundWakeCondition => "inbound",
            HttpWakeCondition => "http",
            ChildRunCompletedWakeCondition => "childRunCompleted",
            AnyOfWakeCondition => "anyOf",
            _ => condition.GetType().Name
        };
    }

    private static string GetWakeConditionMatchKey(WakeCondition condition)
    {
        // Must equal the channel-namespaced correlation key produced by CorrelationKeyResolver
        // for the corresponding inbound channel, so an inbound event can find this bookmark.
        return condition switch
        {
            // Timers aren't matched by an inbound event key — they're woken by ExpiresAt alone.
            TimerWakeCondition => string.Empty,
            SignalWakeCondition signal => $"signal:{signal.Name}:{signal.Correlation}",
            InboundWakeCondition inbound => $"{inbound.DeviceRefId}:{inbound.PropertyName}",
            HttpWakeCondition http => $"http-wake:{http.Token}",
            ChildRunCompletedWakeCondition child => $"child-completed:{child.ChildRunId}",
            AnyOfWakeCondition => "anyOf",
            _ => string.Empty
        };
    }

    private static Guid ResolveForkTargetNodeId(WorkflowDefinition definition, Guid currentNodeId, string outboundPort)
    {
        Guid? resolvedNodeId = ResolveNextNodeId(definition, currentNodeId, outboundPort);
        if (resolvedNodeId is Guid nodeId)
        {
            return nodeId;
        }

        throw new InvalidOperationException($"Unable to resolve fork target port '{outboundPort}' from node {currentNodeId}.");
    }

    private static Guid? ResolveNextNodeId(WorkflowDefinition definition, Guid currentNodeId, string outboundPort)
    {
        if (Guid.TryParse(outboundPort, out Guid jumpNodeId)
            && definition.Nodes.Any(candidate => candidate.NodeId == jumpNodeId))
        {
            return jumpNodeId;
        }

        Edge? edge = definition.Edges.SingleOrDefault(candidate => candidate.From.NodeId == currentNodeId && string.Equals(candidate.From.PortId, outboundPort, StringComparison.Ordinal));
        return edge?.To.NodeId;
    }
}
