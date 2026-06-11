using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Starts a child run of another workflow and parks the parent branch until the child completes.
///
/// It starts the child by submitting a synthetic manual inbound event to <see cref="IInboundHub"/>
/// (which resolves the child's manual trigger registration, creates the child run and dispatches it),
/// then parks on a <see cref="ChildRunCompletedWakeCondition"/> keyed by the child run's RefId. When
/// the child finalizes, RunFinalizer's SubWorkflowCompletionHook emits a "child-completed:{childRunRefId}"
/// event that matches this bookmark and resumes the parent; the child's result is promoted to "childResult".
/// </summary>
internal sealed class SubWorkflowNodeExecutor(IInboundHub hub, IRunProvider runProvider, IClock clock) : INodeExecutor
{
    internal const string ParkedKey = "__subworkflow";
    internal const string WakeKey = "__wake";
    internal const string ChildResultKey = "childResult";
    private const string DefaultPort = "default";

    public string Kind => NodeKind.ControlSubWorkflow;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (SubWorkflowNode)ctx.Node;
        SubWorkflowConfig config = node.Config
            ?? throw new InvalidOperationException($"SubWorkflow node {node.NodeId} is missing config.");

        // Resume visit: the child completed and its result was delivered under "__wake".
        if (ctx.Branch.LocalState.ContainsKey(ParkedKey))
        {
            IReadOnlyDictionary<string, JsonElement> resumePatch = ctx.Branch.LocalState.TryGetValue(WakeKey, out JsonElement wake)
                ? new Dictionary<string, JsonElement> { [ChildResultKey] = wake.Clone() }
                : new Dictionary<string, JsonElement>();
            return new NodeExecutionResult.Continue(DefaultPort, resumePatch);
        }

        // First visit: start the child run via its manual trigger.
        Guid childRefId = config.WorkflowRefId;
        InboundEvent evt = new(
            "manual",
            [$"manual:{childRefId}"],
            $"manual:{childRefId}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["workflowDefinitionRefId"] = JsonSerializer.SerializeToElement(childRefId.ToString()),
                ["body"] = JsonSerializer.SerializeToElement(new
                {
                    parentRunRefId = ctx.Branch.RunRefId,
                    correlationKey = config.CorrelationKey
                })
            },
            clock.UtcNow);

        TriggerDispatchResult result = await hub.HandleAsync(evt, ct);
        if (!result.RunId.HasValue)
        {
            return new NodeExecutionResult.Fail(
                "SUBWORKFLOW_NOT_STARTED",
                $"Child workflow '{childRefId}' did not start (is a manual trigger published?). Outcome: {result.Outcome}.",
                false,
                null);
        }

        RunRow childRun = await runProvider.GetByIdAsync(result.RunId.Value, ct);
        var condition = new ChildRunCompletedWakeCondition(childRun.RefId);
        var patch = new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(childRun.RefId.ToString())
        };

        return new NodeExecutionResult.WaitForBookmark(condition, patch);
    }
}
