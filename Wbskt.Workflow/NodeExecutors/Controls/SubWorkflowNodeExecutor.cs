using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Exceptions;
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
/// <param name="completionHook">
/// Raises the child's completion event again once the parent has parked, for a child that ended first.
/// Optional so the executor's unit tests need no stub.
/// </param>
/// <param name="workflowStates">
/// Says why a child did not start when it is disabled or deprecated. Optional so the executor's unit tests
/// need no stub; without it such a child fails as not started.
/// </param>
/// <param name="counters">
/// Charges the child's start (<see cref="CreditCostOptions.RunStartCost"/>) to the parent's budget just
/// before the child is started. Optional so the executor's unit tests need no stub; without it the start
/// is not charged.
/// </param>
internal sealed class SubWorkflowNodeExecutor(IInboundHub hub, IRunProvider runProvider, TimeProvider clock, IExpressionEvaluator? evaluator = null, IOptions<WorkflowEngineOptions>? options = null, ISubWorkflowCompletionHook? completionHook = null, IWorkflowStateProvider? workflowStates = null, IRunCountersProvider? counters = null, IOptions<CreditCostOptions>? creditCosts = null, ILogger<SubWorkflowNodeExecutor>? logger = null) : ParkingNodeExecutor(clock)
{
    private readonly int _maxDepth = options?.Value.MaxSubWorkflowDepth ?? new WorkflowEngineOptions().MaxSubWorkflowDepth;
    private readonly decimal _runStartCost = (creditCosts?.Value ?? new CreditCostOptions()).RunStartCost;

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
    // Re-parking on the same condition is also what lets the orphan GC's re-raised completion still match it.
    protected override ValueTask<WakeCondition?> KeepWaitingOnAsync(NodeContext ctx, string? waitingOn, DateTime? deadline, CancellationToken ct)
    {
        return ValueTask.FromResult<WakeCondition?>(Guid.TryParse(waitingOn, out Guid parkedOn) ? new ChildRunCompletedWakeCondition(parkedOn) : null);
    }

    protected override async Task<NodeExecutionResult> FirstVisitAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (SubWorkflowNode)ctx.Node;
        SubWorkflowConfig config = node.Config
            ?? throw new InvalidOperationException($"SubWorkflow node {node.NodeId} is missing config.");

        // Start the child run via its manual trigger.
        Guid childRefId = config.WorkflowRefId;

        // A child is started past the plan's active-run ceiling, so nothing else stops a workflow that
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
        catch (Exception ex) when (ex is PermanentNodeException or JsonException)
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

        // A child taken out of service is refused before its start is charged; the same read after the
        // start below covers one disabled in between.
        if (await FindOutOfServiceAsync(childRefId, ctx.Branch.WorkspaceId, ct) is { } outOfService)
        {
            return outOfService;
        }

        // The child's start is paid from the parent's budget through the same budget-checked charge as
        // a step, before it starts, so a child started at the budget's edge cannot take the root, or the
        // cap, past it. When no child starts after all (the ref names nothing, it has no published manual
        // trigger, or it was taken out of service meanwhile), the charge is given back, so a step that
        // starts no child pays only for itself.
        bool startCharged = false;
        if (counters is not null && _runStartCost > 0m)
        {
            CreditCharge? charge = await RetryExecutor.ChargeAsync(counters, (int)ctx.Branch.RunId, _runStartCost, Clock, ct);
            if (charge is null || charge.Refusal is not null)
            {
                return OutOfCredits.Fail(charge?.Refusal ?? CreditRefusal.RunBudget);
            }

            startCharged = true;
        }

        // The parent already holds a place under the plan's active-run ceiling, so its child is
        // started past it (InboundEvent.StartsChildRun). Only a workflow of the parent's own workspace
        // can be the child, whatever ref the step names (InboundEvent.WorkspaceId).
        InboundEvent evt = ManualChannel.Event(childRefId, body, null, Clock.GetUtcNow().UtcDateTime) with
        {
            StartsChildRun = true,
            WorkspaceId = ctx.Branch.WorkspaceId
        };

        // A throw keeps the charge: whether the child started is not known.
        TriggerDispatchResult result = await hub.HandleAsync(evt, ct);
        if (!result.RunId.HasValue && result.Outcome != TriggerDispatchOutcome.Queued && startCharged && counters is not null)
        {
            // Given back whatever the step's token does, since the charge was made. A queued child keeps
            // it: its replay still starts as a child, which RunStarter does not charge again. The refund
            // is held to the budget like any charge, so a run already past its budget (a cap cut since)
            // keeps the cost.
            CreditCharge refund = await RetryExecutor.ChargeAsync(counters, (int)ctx.Branch.RunId, -_runStartCost, Clock, CancellationToken.None);
            if (refund.Refusal is { } refused)
            {
                logger?.LogWarning("The start cost of a sub-workflow step that started no child was not given back to run {RunId}: {Refusal}.", ctx.Branch.RunId, refused);
            }
        }

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
            if (await FindOutOfServiceAsync(childRefId, ctx.Branch.WorkspaceId, ct) is { } takenOutOfService)
            {
                return takenOutOfService;
            }

            return new NodeExecutionResult.Fail(
                "SUBWORKFLOW_NOT_STARTED",
                $"Child workflow '{childRefId}' did not start (is a manual trigger published?). Outcome: {result.Outcome}.",
                false,
                null);
        }

        RunRow childRun = await runProvider.GetByIdAsync(result.RunId.Value, ct);
        return Park(ctx, new ParkPlan(new ChildRunCompletedWakeCondition(childRun.RefId), childRun.RefId.ToString(), null)) with
        {
            // The child runs on its own and can end before the parent's bookmark exists; its event then
            // matched nothing. Read once parked, so one of the two always sees the other.
            AfterParked = afterCt => WakeIfChildEndedAsync(childRun.Id, afterCt)
        };
    }

    // Only a live version has a manual trigger, so a child taken out of service matches nothing.
    // Said by name rather than as a missing trigger, since enabling the child is the fix.
    private async Task<NodeExecutionResult.Fail?> FindOutOfServiceAsync(Guid childRefId, int workspaceId, CancellationToken ct)
    {
        WorkflowStateRow? child = workflowStates is null ? null : await workflowStates.FindAsync(childRefId, workspaceId, ct);
        if (child is not { State: WorkflowState.Disabled or WorkflowState.Deprecated })
        {
            return null;
        }

        bool disabled = child.State == WorkflowState.Disabled;
        return new NodeExecutionResult.Fail(
            disabled ? "SUBWORKFLOW_CHILD_DISABLED" : "SUBWORKFLOW_CHILD_DEPRECATED",
            disabled
                ? $"Child workflow '{childRefId}' did not start: it is disabled. Enable a version of it to call it."
                : $"Child workflow '{childRefId}' did not start: it is deprecated. Reinstate it and enable a version to call it.",
            false,
            null);
    }

    private async Task WakeIfChildEndedAsync(long childRunId, CancellationToken ct)
    {
        if (completionHook is null)
        {
            return;
        }

        RunRow child = await runProvider.GetByIdAsync(childRunId, ct);
        if (RunStatus.IsTerminal(child.Status))
        {
            await completionHook.OnRunCompletedAsync(child.RefId, child.Status, child.OutputJson, ct);
        }
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
                    throw new PermanentNodeException($"Input '{key}' is reserved by the engine and cannot be supplied by the workflow.");
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
