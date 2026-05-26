using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class BranchLoop : IBranchLoop
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
        IIdGenerator idGenerator)
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
            BaseNode? node = definition.Nodes.SingleOrDefault(candidate => candidate.NodeId == branchRow.NodeId);
            if (node is null)
            {
                await CompleteBranchAsync(runRow.Id, branchId, branchRow.RefId, ct);
                return;
            }

            await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, "NodeStarted", ct);

            BranchContext branchContext = BuildBranchContext(branchRow, runRow);
            NodeContext nodeContext = new()
            {
                Branch = branchContext,
                Node = node,
                Providers = _providerComposite,
                Tick = 1,
                ParentResults = null,
                CancellationToken = ct
            };

            INodeExecutor executor = _nodeExecutorRegistry.For(node.Kind);
            NodeExecutionResult result = await executor.ExecuteAsync(nodeContext, ct);

            await AppendEventAsync(runRow.Id, branchRow.RefId, node.NodeId, result is NodeExecutionResult.Fail ? "NodeFailed" : "NodeCompleted", ct);

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
        await AppendEventAsync(runId, branchRefId, null, "BranchCompleted", ct);
    }

    private async Task AppendEventAsync(int runId, Guid branchRefId, Guid? nodeId, string eventKind, CancellationToken ct)
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
                PayloadJson = null,
                Timestamp = _clock.UtcNow
            }
        ], ct);
    }

    private static BranchContext BuildBranchContext(BranchRow branchRow, RunRow runRow)
    {
        return new BranchContext(
            runRow.Id,
            branchRow.Id,
            runRow.WorkflowDefinitionId,
            runRow.WorkflowRefId,
            runRow.WorkflowVersion,
            branchRow.NodeId.ToString(),
            1,
            DeserializeDictionary(branchRow.LocalJson),
            new Dictionary<string, JsonElement>(),
            runRow.CorrelationKey ?? string.Empty,
            runRow.StartedAt);
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

    private static string SerializeLocalState(IReadOnlyDictionary<string, JsonElement> localState)
    {
        return JsonSerializer.Serialize(localState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static Guid? ResolveNextNodeId(WorkflowDefinition definition, Guid currentNodeId, string outboundPort)
    {
        Edge? edge = definition.Edges.SingleOrDefault(candidate => candidate.From.NodeId == currentNodeId && string.Equals(candidate.From.PortId, outboundPort, StringComparison.Ordinal));
        return edge?.To.NodeId;
    }
}
