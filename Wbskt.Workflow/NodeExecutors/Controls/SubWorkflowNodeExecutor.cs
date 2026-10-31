using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.Runtime;

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
/// <param name="options">
/// Optional so the executor's unit tests need no configuration; without it the default depth ceiling applies.
/// </param>
internal sealed class SubWorkflowNodeExecutor(IInboundHub hub, IRunProvider runProvider, TimeProvider clock, IExpressionEvaluator? evaluator = null, IOptions<WorkflowEngineOptions>? options = null) : ParkingNodeExecutor(clock)
{
    private readonly int _maxDepth = options?.Value.MaxSubWorkflowDepth ?? new WorkflowEngineOptions().MaxSubWorkflowDepth;

    internal const string ChildResultKey = "childResult";
    private const string DefaultPort = "default";

    public override string Kind => NodeKind.ControlSubWorkflow;

    // The marker holds the child run's RefId. There is no deadline: the wait has no TTL.
    protected override string ParkedKey => BranchStateKeys.SubWorkflow;

    // The child's result, delivered under "__wake" when it finalizes.
    protected override ResumeStep.Leave? OnWake(NodeContext ctx, string? waitingOn, JsonElement wake)
    {
        return new ResumeStep.Leave(DefaultPort, new Dictionary<string, JsonElement> { [ChildResultKey] = wake.Clone() });
    }

    protected override ResumeStep OnNoWake(NodeContext ctx, string? waitingOn, bool deadlinePassed)
    {
        return new ResumeStep.Leave(DefaultPort);
    }

    // A stale wake is not this child's result: keep waiting on the same child run rather than starting another.
    protected override WakeCondition? KeepWaitingOn(NodeContext ctx, string? waitingOn, DateTime? deadline)
    {
        return Guid.TryParse(waitingOn, out Guid parkedOn) ? new ChildRunCompletedWakeCondition(parkedOn) : null;
    }

    protected override async Task<NodeExecutionResult> FirstVisitAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (SubWorkflowNode)ctx.Node;
        SubWorkflowConfig config = node.Config
            ?? throw new InvalidOperationException($"SubWorkflow node {node.NodeId} is missing config.");

        // Start the child run via its manual trigger.
        Guid childRefId = config.WorkflowRefId;

        // A child is started past the workspace run ceiling, so nothing else stops a workflow that
        // reaches itself (directly, or through another) from starting children forever. Refused before
        // anything is evaluated or started, and not retryable: the same parent would reach the same depth.
        int childDepth = ReadDepth(ctx.Branch.TriggerPayload) + 1;
        if (childDepth > _maxDepth)
        {
            return new NodeExecutionResult.Fail(
                "SUBWORKFLOW_DEPTH_EXCEEDED",
                $"Child workflow '{childRefId}' was not started: it would be nested {childDepth} deep, above the configured ceiling of {_maxDepth} (WorkflowEngine:MaxSubWorkflowDepth). Check for a workflow that starts itself, directly or through another.",
                false,
                null);
        }

        JsonElement body;
        try
        {
            body = await BuildChildBodyAsync(config, ctx, childDepth, ct);
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
        InboundEvent evt = ManualChannel.Event(childRefId, body, null, Clock.GetUtcNow().UtcDateTime) with { StartsChildRun = true };

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
        return Park(ctx, new ParkPlan(new ChildRunCompletedWakeCondition(childRun.RefId), childRun.RefId.ToString(), null));
    }

    /// <summary>
    /// Builds the child's <c>$trigger.body</c>: the engine's own three keys, plus each configured input
    /// evaluated against the parent branch.
    /// </summary>
    private async Task<JsonElement> BuildChildBodyAsync(SubWorkflowConfig config, NodeContext ctx, int childDepth, CancellationToken ct)
    {
        var body = new Dictionary<string, JsonElement>
        {
            [SubWorkflowConfig.ParentRunRefIdKey] = JsonSerializer.SerializeToElement(ctx.Branch.RunRefId),
            ["correlationKey"] = JsonSerializer.SerializeToElement(config.CorrelationKey),
            [SubWorkflowConfig.DepthKey] = JsonSerializer.SerializeToElement(childDepth)
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

                body[key] = (await evaluator.EvaluateForStepAsync(expression, ctx, $"input.{key}", ct)).Clone();
            }
        }

        return JsonSerializer.SerializeToElement(body);
    }

    /// <summary>
    /// How deeply the run this branch belongs to is nested. The depth travels in the trigger payload the
    /// run was started with - for a child, the body its parent built - which is persisted on the root
    /// branch's local state and inherited by every branch forked from it, so it reads the same after a
    /// restart. A run started by anything but a sub-workflow step carries none and is depth 0, and so is
    /// one whose body holds anything but a positive whole number there: the engine writes the value for
    /// every child it starts, so only a body supplied from outside can hold a malformed one.
    /// </summary>
    private static int ReadDepth(IReadOnlyDictionary<string, JsonElement> triggerPayload)
    {
        if (triggerPayload.TryGetValue("body", out JsonElement body)
            && body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(SubWorkflowConfig.DepthKey, out JsonElement depth)
            && depth.ValueKind == JsonValueKind.Number
            && depth.TryGetInt32(out int value)
            && value > 0)
        {
            return value;
        }

        return 0;
    }
}
