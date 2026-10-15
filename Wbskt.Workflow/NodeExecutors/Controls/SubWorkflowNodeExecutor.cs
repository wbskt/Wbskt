using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

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
/// <param name="evaluator">
/// Optional so the executor's existing unit tests need no stub. Only required when the node actually
/// configures inputs; without one, an input-carrying node fails with a clear message rather than
/// quietly starting the child with an empty payload.
/// </param>
internal sealed class SubWorkflowNodeExecutor(IInboundHub hub, IRunProvider runProvider, TimeProvider clock, IExpressionEvaluator? evaluator = null) : INodeExecutor
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

        JsonElement body;
        try
        {
            body = await BuildChildBodyAsync(config, ctx, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An input that cannot be evaluated is an authoring error, not a transient one - retrying
            // would produce the same failure - and starting the child without it would hand it a
            // silently incomplete payload, which is worse than not starting it.
            return new NodeExecutionResult.Fail(
                "SUBWORKFLOW_INPUT_EVALUATION_ERROR",
                $"An input to child workflow '{childRefId}' could not be evaluated: {ex.Message}",
                false,
                null);
        }

        // The parent already holds a place under the workspace's active-run ceiling, so its child is
        // started past it (InboundEvent.StartsChildRun).
        InboundEvent evt = ManualChannel.Event(childRefId, body, null, clock.GetUtcNow().UtcDateTime) with { StartsChildRun = true };

        TriggerDispatchResult result = await hub.HandleAsync(evt, ct);
        if (result.Outcome == TriggerDispatchOutcome.Refused)
        {
            // Not expected, since a child is exempt from the ceiling; kept as a safety net. Not
            // retryable: a sub-workflow step is a control node and carries no retry policy, so nothing
            // would ever retry it, and saying otherwise misleads whoever reads the failure. It fails
            // the parent with a code that says why rather than as a missing manual trigger.
            return new NodeExecutionResult.Fail(
                "SUBWORKFLOW_WORKSPACE_BUSY",
                $"Child workflow '{childRefId}' did not start: the workspace has as many runs going as it may.",
                false,
                null);
        }

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

    /// <summary>
    /// Builds the child's <c>$trigger.body</c>: the engine's own two keys, plus each configured input
    /// evaluated against the parent branch.
    /// </summary>
    private async Task<JsonElement> BuildChildBodyAsync(SubWorkflowConfig config, NodeContext ctx, CancellationToken ct)
    {
        var body = new Dictionary<string, JsonElement>
        {
            ["parentRunRefId"] = JsonSerializer.SerializeToElement(ctx.Branch.RunRefId),
            ["correlationKey"] = JsonSerializer.SerializeToElement(config.CorrelationKey)
        };

        if (config.Input is { Count: > 0 })
        {
            if (evaluator is null)
            {
                throw new InvalidOperationException("No expression evaluator is available to evaluate sub-workflow inputs.");
            }

            foreach ((string key, WorkflowExpression expression) in config.Input)
            {
                // Reserved keys are rejected at publish (WF-19); this is the belt-and-braces half, so a
                // definition that predates the rule cannot overwrite the child's route home.
                if (SubWorkflowConfig.ReservedInputKeys.Contains(key))
                {
                    throw new InvalidOperationException($"Input '{key}' is reserved by the engine and cannot be supplied by the workflow.");
                }

                body[key] = (await evaluator.EvaluateAsync(expression, ctx.Branch, ct)).Clone();
            }
        }

        return JsonSerializer.SerializeToElement(body);
    }
}
