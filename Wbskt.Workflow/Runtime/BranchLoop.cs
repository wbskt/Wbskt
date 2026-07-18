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
        ILogger<BranchLoop>? logger = null)
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

        await AppendEventAsync(runRow.Id, branchRow.RefId, null, "BranchStarted", ct);
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
            
            // [RJ]: what is PendingTakePort?
            if (!string.IsNullOrEmpty(branchRow.PendingTakePort))
            {
                result = new NodeExecutionResult.Continue(branchRow.PendingTakePort, new Dictionary<string, JsonElement>());
            }
            else
            {
                await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, "NodeStarted", ct);
                _logger?.LogInformation("Executing node {NodeId} ({NodeKind}) on run {RunId} branch {BranchId}", node.NodeId, node.Kind, runId, branchId);

                INodeExecutor executor = _nodeExecutorRegistry.For(node.Kind);
                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                string outcome = "Succeeded";
                try
                {
                    result = await RetryExecutor.RunWithRetryAsync(
                        node,
                        branchContext,
                        executor,
                        new NodeExecutionServices(_providerComposite),
                        _clock,
                        linkedToken,
                        _runProvider,
                        _runCountersProvider,
                        _creditCostCalculator,
                        workflowMetrics: _workflowMetrics);
                }
                catch (EngineFaultException ex)
                {
                    _logger?.LogError(ex, "Engine fault occurred executing node {NodeId} in branch {BranchId} for run {RunId}", branchRow.NodeId, branchId, runId);
                    outcome = "Failed";
                    // [RJ]: TODO: "Faulted" is not a valid status as per the table definition "dbo.Run"
                    // [RJ]: EDIT: it was just the valued to be indexed. not the actual allowed statuses
                    await _runProvider.TransitionStatusAsync(runRow.Id, runRow.Status, "Faulted", ct);
                    string faultJson = JsonSerializer.Serialize(new { ex.Message }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, "RunFaulted", faultJson, ct);
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

                string? eventPayload = result switch
                {
                    NodeExecutionResult.Fail fail => JsonSerializer.Serialize(new { fail.ErrorCode, fail.Message }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    NodeExecutionResult.Continue cont => JsonSerializer.Serialize(new { port = cont.OutboundPort, output = cont.LocalStatePatch }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    _ => null
                };
                
                _logger?.LogInformation("Node {NodeId} ({NodeKind}) completed with outcome {Outcome} on run {RunId} branch {BranchId}", node.NodeId, node.Kind, result.GetType().Name, runId, branchId);
                await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, result is NodeExecutionResult.Fail ? "NodeFailed" : "NodeCompleted", eventPayload, ct);
            }

            if (linkedToken.IsCancellationRequested && result is not NodeExecutionResult.Terminal)
            {
                _logger?.LogInformation("Execution cancelled for node {NodeId} in branch {BranchId} for run {RunId} after executor returned", branchRow.NodeId, branchId, runId);
                result = new NodeExecutionResult.Terminal(BranchTerminalReason.Cancelled);
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

                    string localJson = SerializeLocalState(MergeLocalState(branchRow.LocalJson, @continue.LocalStatePatch));
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
                    await AppendEventAsync(runRow.Id, branchRow.RefId, branchRow.NodeId, "BranchParked", ct);

                    DateTime nowUtc = _clock.UtcNow;
                    await _bookmarkProvider.CreateAsync(CreateBookmarkRow(runRow.Id, branchRow.RefId, branchRow.NodeId, wait.Condition, nowUtc), ct);

                    if (wait.Condition.Ttl is TimeSpan ttl)
                    {
                        var companionCondition = new TimerWakeCondition(nowUtc + ttl)
                        {
                            TtlPort = wait.Condition.TtlPort
                        };
                        await _bookmarkProvider.CreateAsync(
                            CreateBookmarkRow(runRow.Id, branchRow.RefId, branchRow.NodeId, companionCondition, nowUtc),
                            ct);
                    }

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

                        string localJson = SerializeLocalState(MergeLocalState(branchRow.LocalJson, handledContinue.LocalStatePatch));
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
                        await _runProvider.TransitionStatusAsync(runRow.Id, "Running", "Failing", ct);
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
                            await _runProvider.TransitionStatusAsync(runRow.Id, "Running", "Failing", ct);
                        }
                    }

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

    private async Task CompleteBranchAsync(int runId, long branchId, Guid branchRefId, CancellationToken ct)
    {
        await _branchProvider.SetCompletedAsync(branchId, ct);
        int postDecrementCount = await _runCountersProvider.IncrementActiveBranchesAsync(runId, -1, ct);
        await AppendEventAsync(runId, branchRefId, null, "BranchCompleted", ct);

        if (postDecrementCount == 0 && _runFinalizer is not null)
        {
            await _runFinalizer.FinalizeAsync(runId, ct);
        }
    }

    private async Task CancelBranchAsync(int runId, long branchId, BranchRow branchRow, CancellationToken ct)
    {
        await _branchProvider.UpdatePointerAsync(branchId, branchRow.NodeId, "Cancelled", branchRow.LocalJson, branchRow.LastOutputJson, ct);
        int postDecrementCount = await _runCountersProvider.IncrementActiveBranchesAsync(runId, -1, ct);
        await AppendEventAsync(runId, branchRow.RefId, null, "BranchCancelled", ct);

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
                Severity = eventKind == "NodeFailed" ? "Warn" : "Info",
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
            BranchRefId = branchRow.RefId
        };
    }

    private static IReadOnlyDictionary<string, JsonElement> MergeLocalState(string existingJson, IReadOnlyDictionary<string, JsonElement> patch)
    {
        Dictionary<string, JsonElement> merged = new(DeserializeDictionary(existingJson), StringComparer.Ordinal);
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

    private BookmarkRow CreateBookmarkRow(int runId, Guid branchRefId, Guid nodeId, WakeCondition condition, DateTime createdAt)
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
            ExpiresAt = GetWakeConditionExpiresAt(condition),
            TtlPort = condition.TtlPort,
            CreatedAt = createdAt
        };
    }

    private static DateTime? GetWakeConditionExpiresAt(WakeCondition condition)
    {
        return condition switch
        {
            TimerWakeCondition timer => timer.At,
            _ => null
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
            TimerWakeCondition timer => timer.At.ToString("O"),
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
