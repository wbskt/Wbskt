using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

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
        ICompensationOrchestrator? compensationOrchestrator = null)
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
        _runFinalizer = runFinalizer;
        _runCancellationService = runCancellationService ?? new NoOpRunCancellationService();
        _compensationOrchestrator = compensationOrchestrator;
    }

    public async Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken ct)
    {
        BranchRow branchRow = await _branchProvider.GetByIdAsync(branchId, ct);
        RunRow runRow = await _runProvider.GetByIdAsync(runId, ct);
        WorkflowDefinition definition = await _workflowDefinitionCache.GetAsync(runRow.WorkflowDefinitionId, ct);

        await AppendEventAsync(runRow.Id, branchRow.RefId, null, "BranchStarted", ct);

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

            await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, "NodeStarted", ct);

            BranchContext branchContext = BuildBranchContext(branchRow, runRow);
            INodeExecutor executor = _nodeExecutorRegistry.For(node.Kind);
            NodeExecutionResult result;
            try
            {
                result = await RetryExecutor.RunWithRetryAsync(
                    node,
                    branchContext,
                    executor,
                    new NodeExecutionServices(_providerComposite),
                    _clock,
                    ct);
            }
            catch (Exception ex)
            {
                result = new NodeExecutionResult.Fail("EXECUTOR_CRASH", ex.Message, false, ex);
            }

            string? eventPayload = result switch
            {
                NodeExecutionResult.Fail fail => JsonSerializer.Serialize(new { fail.ErrorCode, fail.Message }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                NodeExecutionResult.Continue cont => JsonSerializer.Serialize(new { port = cont.OutboundPort, output = cont.LocalStatePatch }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                _ => null
            };
            await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, result is NodeExecutionResult.Fail ? "NodeFailed" : "NodeCompleted", eventPayload, ct);

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
                    if (fork.Children.Count == 1 && string.IsNullOrWhiteSpace(fork.ContinueNodeId))
                    {
                        ForkSpec child = fork.Children.Single();
                        string childLocalJson = SerializeLocalState(MergeLocalState(SerializeLocalState(baseLocalState), child.LocalState));
                        branchRow = await _branchProvider.UpdatePointerAsync(branchId, ResolveForkTargetNodeId(definition, node.NodeId, child.NodeId), ActiveStatus, childLocalJson, branchRow.LastOutputJson, ct);
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
                                NodeId = ResolveForkTargetNodeId(definition, node.NodeId, child.NodeId),
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

                    if (string.IsNullOrWhiteSpace(fork.ContinueNodeId))
                    {
                        await CompleteBranchAsync(runRow.Id, branchId, branchRow.RefId, ct);
                        return;
                    }

                    string continueLocalJson = SerializeLocalState(baseLocalState);
                    branchRow = await _branchProvider.UpdatePointerAsync(branchId, ResolveForkTargetNodeId(definition, node.NodeId, fork.ContinueNodeId), ActiveStatus, continueLocalJson, branchRow.LastOutputJson, ct);
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
                        await _bookmarkProvider.CreateAsync(
                            CreateBookmarkRow(runRow.Id, branchRow.RefId, branchRow.NodeId, new TimerWakeCondition(nowUtc + ttl), nowUtc),
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
                    await _branchProvider.SetFailedAsync(branchId, errorJson, ct);
                    if (definition.FailFast)
                    {
                        await _runProvider.TransitionStatusAsync(runRow.Id, "Running", "Failing", ct);
                    }

                    if (definition.RunCompensationOnFailure && _compensationOrchestrator is not null)
                    {
                        await _compensationOrchestrator.RunAsync(runRow.Id, branchId, ct);
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

    private static BranchContext BuildBranchContext(BranchRow branchRow, RunRow runRow)
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
            runRow.StartedAt)
        {
            RunRefId = runRow.RefId
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
            TtlPort = null,
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

    private static Guid ResolveForkTargetNodeId(WorkflowDefinition definition, Guid currentNodeId, string target)
    {
        Guid? resolvedNodeId = ResolveNextNodeId(definition, currentNodeId, target);
        if (resolvedNodeId is Guid nodeId)
        {
            return nodeId;
        }

        throw new InvalidOperationException($"Unable to resolve fork target '{target}' from node {currentNodeId}.");
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
