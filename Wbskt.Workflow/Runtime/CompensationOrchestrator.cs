using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class CompensationOrchestrator : ICompensationOrchestrator
{
    private readonly IRunProvider _runProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IHistoryEventProvider _historyEventProvider;
    private readonly IWorkflowDefinitionCache _workflowDefinitionCache;
    private readonly INodeExecutorRegistry _nodeExecutorRegistry;
    private readonly IProviderComposite _providerComposite;
    private readonly IClock _clock;

    public CompensationOrchestrator(
        IRunProvider runProvider,
        IBranchProvider branchProvider,
        IHistoryEventProvider historyEventProvider,
        IWorkflowDefinitionCache workflowDefinitionCache,
        INodeExecutorRegistry nodeExecutorRegistry,
        IProviderComposite providerComposite,
        IClock clock)
    {
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _historyEventProvider = historyEventProvider;
        _workflowDefinitionCache = workflowDefinitionCache;
        _nodeExecutorRegistry = nodeExecutorRegistry;
        _providerComposite = providerComposite;
        _clock = clock;
    }

    public async Task RunAsync(long runId, long branchId, CancellationToken ct)
    {
        RunRow run = await _runProvider.GetByIdAsync(runId, ct);
        BranchRow branch = await _branchProvider.GetByIdAsync(branchId, ct);
        WorkflowDefinition definition = await _workflowDefinitionCache.GetAsync(run.WorkflowDefinitionId, ct);
        IReadOnlyCollection<HistoryEventRow> history = await _historyEventProvider.GetByRunIdAsync(run.Id, 0, int.MaxValue, ct);
        var compensationTargets = history
            .Where(evt => evt.RunId == run.Id
                && evt.BranchRefId == branch.RefId
                && string.Equals(evt.EventKind, "NodeCompleted", StringComparison.Ordinal)
                && evt.NodeId is not null)
            .Select(evt => new { Event = evt, Node = definition.Nodes.OfType<BaseActionNode>().SingleOrDefault(node => node.NodeId == evt.NodeId && node.Compensation is not null) })
            .Where(item => item.Node is not null)
            .OrderBy(item => item.Event.HistoryEventId)
            .ToArray();

        foreach (var item in compensationTargets)
        {
            try
            {
                await ExecuteCompensationAsync(run, branch, item.Node!, item.Event, ct);
                await AppendCompensationEventAsync(run.Id, branch.RefId, item.Event.NodeId, item.Node!.Compensation!, ct);
            }
            catch
            {
            }
        }
    }

    private async Task ExecuteCompensationAsync(RunRow run, BranchRow branch, BaseActionNode node, HistoryEventRow historyEvent, CancellationToken ct)
    {
        CompensationDeclaration compensation = node.Compensation!;
        CompensationNode compensationNode = new(node.NodeId, compensation.Kind, compensation.Config);
        INodeExecutor executor = _nodeExecutorRegistry.For(compensation.Kind);
        NodeContext context = new()
        {
            Branch = new BranchContext(
                run.Id,
                branch.Id,
                run.WorkflowDefinitionId,
                run.WorkflowRefId,
                run.WorkflowVersion,
                node.NodeId.ToString(),
                1,
                ParseLocalState(historyEvent.PayloadJson),
                new Dictionary<string, JsonElement>(),
                run.CorrelationKey ?? string.Empty,
                run.StartedAt),
            Node = compensationNode,
            Providers = _providerComposite,
            Tick = 1,
            ParentResults = null,
            CancellationToken = ct
        };

        await executor.ExecuteAsync(context, ct);
    }

    private async Task AppendCompensationEventAsync(int runId, Guid branchRefId, Guid? nodeId, CompensationDeclaration compensation, CancellationToken ct)
    {
        await _historyEventProvider.InsertBatchAsync(
        [
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = runId,
                BranchRefId = branchRefId,
                NodeId = nodeId,
                EventKind = "CompensationExecuted",
                Severity = "Info",
                PayloadJson = JsonSerializer.Serialize(new { compensation.Kind }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Timestamp = _clock.UtcNow
            }
        ], ct);
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseLocalState(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new Dictionary<string, JsonElement>();
        }

        using JsonDocument document = JsonDocument.Parse(payloadJson);
        if (!document.RootElement.TryGetProperty("localState", out JsonElement localState))
        {
            return new Dictionary<string, JsonElement>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(localState.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? new Dictionary<string, JsonElement>();
    }

    private sealed record CompensationNode(Guid NodeId, string KindValue, JsonElement? Config) : BaseNode(NodeId, $"compensation:{KindValue}", Array.Empty<PortDefinition>())
    {
        public override string Kind => KindValue;
    }
}
