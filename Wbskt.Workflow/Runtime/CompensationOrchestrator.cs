using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private readonly IRunJournal _journal;
    private readonly IWorkflowDefinitionCache _workflowDefinitionCache;
    private readonly INodeExecutorRegistry _nodeExecutorRegistry;
    private readonly TimeProvider _clock;
    private readonly ILogger<CompensationOrchestrator>? _logger;

    public CompensationOrchestrator(
        IRunProvider runProvider,
        IBranchProvider branchProvider,
        IRunJournal journal,
        IWorkflowDefinitionCache workflowDefinitionCache,
        INodeExecutorRegistry nodeExecutorRegistry,
        TimeProvider clock,
        ILogger<CompensationOrchestrator>? logger = null)
    {
        _runProvider = runProvider;
        _branchProvider = branchProvider;
        _journal = journal;
        _workflowDefinitionCache = workflowDefinitionCache;
        _nodeExecutorRegistry = nodeExecutorRegistry;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Records a compensation step that failed. Guarded: the caller is already handling a failure and
    /// must not be derailed by the attempt to write about it.
    /// </summary>
    private async Task TryAppendCompensationFailedAsync(int runId, Guid branchRefId, Guid? nodeId, Exception cause, CancellationToken ct)
    {
        try
        {
            await _journal.AppendAsync(
                new RunJournalEntry(runId, HistoryEventKind.CompensationFailed) { BranchRefId = branchRefId, NodeId = nodeId, Payload = new { cause.Message } },
                ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to record a CompensationFailed history event for run {RunId}.", runId);
        }
    }

    public async Task RunAsync(long runId, long branchId, CancellationToken ct)
    {
        RunRow run = await _runProvider.GetByIdAsync(runId, ct);
        BranchRow branch = await _branchProvider.GetByIdAsync(branchId, ct);
        WorkflowDefinition definition = await _workflowDefinitionCache.GetAsync(run.WorkflowDefinitionId, ct);
        IReadOnlyCollection<HistoryEventRow> history = await _journal.ReadAllAsync(run.Id, ct);
        var compensationTargets = history
            .Where(evt => evt.RunId == run.Id
                && evt.BranchRefId == branch.RefId
                && string.Equals(evt.EventKind, HistoryEventKind.NodeCompleted, StringComparison.Ordinal)
                && evt.NodeId is not null)
            .Select(evt => new { Event = evt, Node = definition.Nodes.OfType<BaseActionNode>().SingleOrDefault(node => node.NodeId == evt.NodeId && node.Compensation is not null) })
            .Where(item => item.Node is not null)
            .OrderByDescending(item => item.Event.HistoryEventId)
            .ToArray();

        var tasks = compensationTargets.Select(async item =>
        {
            try
            {
                await ExecuteCompensationAsync(run, branch, item.Node!, item.Event, definition.WorkspaceId, ct);
                await AppendCompensationEventAsync(run.Id, branch.RefId, item.Event.NodeId, item.Node!.Compensation!, ct);
            }
            catch (Exception ex)
            {
                // Per spec a failed compensation step does not block the others - but swallowing it
                // silently meant an undo that never happened left no trace at all, which is exactly
                // the thing an operator needs to know about.
                _logger?.LogWarning(ex, "Compensation for node {NodeId} on run {RunId} failed: {Message}", item.Event.NodeId, run.Id, ex.Message);
                await TryAppendCompensationFailedAsync(run.Id, branch.RefId, item.Event.NodeId, ex, ct);
            }
        });
        await Task.WhenAll(tasks);
    }

    private async Task ExecuteCompensationAsync(RunRow run, BranchRow branch, BaseActionNode node, HistoryEventRow historyEvent, int workspaceId, CancellationToken ct)
    {
        CompensationDeclaration compensation = node.Compensation!;
        
        string json = JsonSerializer.Serialize(new
        {
            nodeId = node.NodeId,
            name = $"Compensation for {node.Name}",
            ports = Array.Empty<PortDefinition>(),
            kind = compensation.Kind,
            config = compensation.Config
        }, EngineJson.Options);
        
        BaseNode compensationNode = JsonSerializer.Deserialize<BaseNode>(json, EngineJson.Options)!;

        INodeExecutor executor = _nodeExecutorRegistry.For(compensation.Kind);
        IReadOnlyDictionary<string, JsonElement> triggerPayload;
        IReadOnlyDictionary<string, JsonElement> branchLocalState = string.IsNullOrWhiteSpace(branch.LocalJson) 
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(branch.LocalJson, EngineJson.Options) ?? new Dictionary<string, JsonElement>();

        if (branchLocalState.TryGetValue("trigger", out JsonElement triggerElement)
            && triggerElement.ValueKind == JsonValueKind.Object)
        {
            triggerPayload = triggerElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }
        else
        {
            triggerPayload = new Dictionary<string, JsonElement>();
        }

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
                triggerPayload,
                run.CorrelationKey ?? string.Empty,
                run.StartedAt,
                workspaceId)
            {
                RunRefId = run.RefId,
                BranchRefId = branch.RefId
            },
            Node = compensationNode,
            Tick = 1,
            ParentResults = null,
            CancellationToken = ct,
            IdempotencyKey = $"compensation:{run.Id}:{branch.RefId:N}:{node.NodeId:N}"
        };

        NodeExecutionResult result = await executor.ExecuteAsync(context, ct);
        if (result is NodeExecutionResult.Fail fail)
        {
            throw new InvalidOperationException($"Compensation failed: {fail.ErrorCode} - {fail.Message}");
        }
    }

    private async Task AppendCompensationEventAsync(int runId, Guid branchRefId, Guid? nodeId, CompensationDeclaration compensation, CancellationToken ct)
    {
        await _journal.AppendAsync(
            new RunJournalEntry(runId, HistoryEventKind.CompensationExecuted) { BranchRefId = branchRefId, NodeId = nodeId, Payload = new { compensation.Kind } },
            ct);
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseLocalState(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new Dictionary<string, JsonElement>();
        }

        using JsonDocument document = JsonDocument.Parse(payloadJson);
        if (document.RootElement.TryGetProperty("output", out JsonElement output))
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(output.GetRawText(), EngineJson.Options)
                ?? new Dictionary<string, JsonElement>();
        }

        if (document.RootElement.TryGetProperty("localState", out JsonElement localState))
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(localState.GetRawText(), EngineJson.Options)
                ?? new Dictionary<string, JsonElement>();
        }

        return new Dictionary<string, JsonElement>();
    }
}
