using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Reads and writes workflow state.
///
/// <para><b>Set is last-writer-wins.</b> It used to run a three-attempt compare-and-set loop that
/// could fail the node outright under contention (SHARED_VAR_CAS_FAILED) - the opposite of the
/// intended semantics. Counters use the atomic Increment/Decrement procedures instead, which need no
/// retry loop because the arithmetic happens inside the UPDATE.</para>
/// </summary>
internal sealed class VariableNodeExecutor : INodeExecutor
{
    private const string JsonVarType = "Json";

    /// <summary>Branch-state key carrying whether the last CompareAndSet on this branch won its race.</summary>
    internal const string CompareAndSetResultKey = "casSucceeded";

    private static readonly JsonSerializerOptions ExpressionJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ISharedVariableProvider _sharedVariableProvider;
    private readonly IExpressionEvaluator _expressionEvaluator;
    private readonly IVariableWritePublisher? _writes;

    public VariableNodeExecutor(ISharedVariableProvider sharedVariableProvider, IExpressionEvaluator expressionEvaluator, IVariableWritePublisher? writes = null)
    {
        _sharedVariableProvider = sharedVariableProvider;
        _expressionEvaluator = expressionEvaluator;
        _writes = writes;
    }

    public string Kind => NodeKind.ControlVariable;

    /// <summary>
    /// A local write lands in the branch row and is replayed with it, so every local operation is safe.
    /// A shared write happens outside the branch row, before the pointer moves, so a crash in between
    /// replays it: a Set writes the same value again, which is harmless, but an Increment or Decrement
    /// would count twice, and a CompareAndSet that already won would now miss its own write and report
    /// a lost race - which an optimistic retry loop answers by writing again. Those three claim an
    /// idempotency key so the replay returns the first outcome instead.
    /// </summary>
    public bool IsReplaySafe(BaseNode node)
    {
        if (node is not VariableNode { Config: { } config })
        {
            // A node without config fails before it writes anything.
            return true;
        }

        return config.Scope == VariableScope.Local || config.Op == VariableOperation.Set;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not VariableNode node || node.Config is null)
        {
            return new NodeExecutionResult.Fail("VARIABLE_CONFIG_INVALID", "Variable node config is required.", false, null);
        }

        VariableConfig config = node.Config;

        return config.Op switch
        {
            VariableOperation.Set => await ExecuteSetAsync(ctx, config, ct),
            VariableOperation.Increment => await ExecuteCounterAsync(ctx, config, negate: false, ct),
            VariableOperation.Decrement => await ExecuteCounterAsync(ctx, config, negate: true, ct),

            VariableOperation.CompareAndSet => await ExecuteCompareAndSetAsync(ctx, config, ct),

            _ => new NodeExecutionResult.Fail(
                "VARIABLE_OPERATION_NOT_SUPPORTED",
                $"Variable operation {config.Op} is not implemented.",
                false,
                null)
        };
    }

    private async Task<NodeExecutionResult> ExecuteSetAsync(NodeContext ctx, VariableConfig config, CancellationToken ct)
    {
        JsonElement value = await ResolveValueAsync(ctx, config, ct);

        if (config.Scope == VariableScope.Local)
        {
            return Continue(new Dictionary<string, JsonElement> { [config.Var] = value });
        }

        string valueJson = CanonicalJson.Serialize(value);
        try
        {
            await _sharedVariableProvider.SetAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, valueJson, ct);
        }
        catch (KeyNotFoundException)
        {
            // SharedVariable_Set only UPDATEs, so a variable that was never written needs creating.
            await _sharedVariableProvider.InitializeAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, JsonVarType, valueJson, ct);
        }

        await AnnounceAsync(ctx, config, valueJson, ct);
        return Continue();
    }

    /// <summary>
    /// Writes only if the variable currently holds <see cref="VariableConfig.Expected"/>.
    /// </summary>
    /// <remarks>
    /// A losing compare is a <em>normal</em> outcome, not a node failure - somebody else won the race,
    /// which is the situation the operation exists to detect. The result lands in branch state under
    /// <see cref="CompareAndSetResultKey"/> so a Logic gate can branch on it and, typically, loop back
    /// to retry. Failing the node instead would make the operation useless for the optimistic-concurrency
    /// pattern it is for.
    /// </remarks>
    private async Task<NodeExecutionResult> ExecuteCompareAndSetAsync(NodeContext ctx, VariableConfig config, CancellationToken ct)
    {
        if (config.Expected is null)
        {
            return new NodeExecutionResult.Fail(
                "VARIABLE_EXPECTED_MISSING",
                $"CompareAndSet on '{config.Var}' needs an 'expected' value to compare against.",
                false,
                null);
        }

        JsonElement newValue = await ResolveValueAsync(ctx, config.Value, "value", ct);
        JsonElement expected = await ResolveValueAsync(ctx, config.Expected, "expected", ct);

        // Comparison is on canonical JSON text, because SharedVariable_CompareAndSet compares text in SQL
        // (ValueJson = @Expected) and every shared write stores the canonical form. Local compares the
        // same way, so the two scopes agree on what "equal" means.
        string expectedJson = CanonicalJson.Serialize(expected);
        string newValueJson = CanonicalJson.Serialize(newValue);

        bool succeeded;
        if (config.Scope == VariableScope.Local)
        {
            string currentJson = ctx.Branch.LocalState.TryGetValue(config.Var, out JsonElement current)
                ? CanonicalJson.Serialize(current)
                : CanonicalJson.Serialize(JsonSerializer.SerializeToElement((string?)null));

            succeeded = string.Equals(currentJson, expectedJson, StringComparison.Ordinal);
            var patch = new Dictionary<string, JsonElement>
            {
                [CompareAndSetResultKey] = JsonSerializer.SerializeToElement(succeeded)
            };

            if (succeeded)
            {
                patch[config.Var] = newValue;
            }

            return Continue(patch);
        }

        succeeded = await _sharedVariableProvider.CompareAndSetAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, expectedJson, newValueJson, ct) > 0;

        // A value stored before writes were canonical keeps its original spelling until it is next
        // written, so a miss is retried once against that spelling. Each attempt is atomic and a miss
        // writes nothing, so the retry cannot apply the new value twice.
        string legacyExpectedJson = JsonSerializer.Serialize(expected);
        if (!succeeded && !string.Equals(legacyExpectedJson, expectedJson, StringComparison.Ordinal))
        {
            succeeded = await _sharedVariableProvider.CompareAndSetAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, legacyExpectedJson, newValueJson, ct) > 0;
        }

        if (succeeded)
        {
            await AnnounceAsync(ctx, config, newValueJson, ct);
        }

        return Continue(new Dictionary<string, JsonElement>
        {
            [CompareAndSetResultKey] = JsonSerializer.SerializeToElement(succeeded)
        });
    }

    private async Task<NodeExecutionResult> ExecuteCounterAsync(NodeContext ctx, VariableConfig config, bool negate, CancellationToken ct)
    {
        JsonElement value = await ResolveValueAsync(ctx, config, ct);

        // The step defaults to 1, so a bare Increment node needs no configured value.
        double step = 1;
        if (value.ValueKind == JsonValueKind.Number)
        {
            step = value.GetDouble();
        }
        else if (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return new NodeExecutionResult.Fail(
                "VARIABLE_STEP_NOT_NUMERIC",
                $"{config.Op} needs a numeric value, but the configured value is {value.ValueKind}.",
                false,
                null);
        }

        long delta = (long)(negate ? -step : step);
        if (delta != (negate ? -step : step))
        {
            // Counters move in whole steps; the fraction is dropped, which reads as a counter that
            // "doesn't count" when the step is below 1.
            ctx.Warnings.Add(new NodeWarning(HistoryEventKind.CounterStepTruncated, new { var = config.Var, step, applied = negate ? -delta : delta }));
        }

        if (config.Scope == VariableScope.Local)
        {
            double current = 0;
            if (ctx.Branch.LocalState.TryGetValue(config.Var, out JsonElement existing))
            {
                if (existing.ValueKind == JsonValueKind.Number)
                {
                    current = existing.GetDouble();
                }
                else if (existing.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    // A variable that does not exist yet starting at 0 is expected; one that holds text or
                    // an object being counted from 0 loses what it held.
                    ctx.Warnings.Add(new NodeWarning(HistoryEventKind.CounterReset, new { var = config.Var, kind = existing.ValueKind.ToString() }));
                }
            }

            return Continue(new Dictionary<string, JsonElement>
            {
                [config.Var] = JsonSerializer.SerializeToElement(current + delta)
            });
        }

        // Atomic in the procedure, which also creates a missing counter at the step - no read-modify-write
        // race and no retry loop. A variable that is not a counter throws SharedVariableNotACounterException,
        // which fails the node as VARIABLE_NOT_A_COUNTER.
        string counted = delta >= 0
            ? await _sharedVariableProvider.IncrementAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, delta, ct)
            : await _sharedVariableProvider.DecrementAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, -delta, ct);

        await AnnounceAsync(ctx, config, counted, ct);
        return Continue();
    }

    /// <summary>Names this run as the writer of a shared variable, for the audit log.</summary>
    private Task AnnounceAsync(NodeContext ctx, VariableConfig config, string valueJson, CancellationToken ct)
    {
        return _writes is null
            ? Task.CompletedTask
            : _writes.PublishAsync(new VariableWrite(
                ctx.Branch.WorkflowDefinitionRefId, ctx.Branch.WorkflowDefinitionId, ctx.Branch.RunRefId, ctx.Branch.WorkspaceId,
                config.Var, config.Op.ToString(), valueJson), ct);
    }

    private Task<JsonElement> ResolveValueAsync(NodeContext ctx, VariableConfig config, CancellationToken ct)
    {
        return ResolveValueAsync(ctx, config.Value, "value", ct);
    }

    /// <summary>
    /// Resolves a configured value, evaluating it when it is an expression tree (detected by the
    /// polymorphic "kind" discriminator) rather than a plain JSON literal.
    /// </summary>
    private async Task<JsonElement> ResolveValueAsync(NodeContext ctx, JsonElement? configured, string field, CancellationToken ct)
    {
        JsonElement value = configured?.Clone() ?? JsonSerializer.SerializeToElement((string?)null);

        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("kind", out JsonElement kindProperty)
            && kindProperty.ValueKind == JsonValueKind.String)
        {
            var expression = JsonSerializer.Deserialize<WorkflowExpression>(value.GetRawText(), ExpressionJsonOptions);
            if (expression is not null)
            {
                value = await _expressionEvaluator.EvaluateForStepAsync(expression, ctx, field, ct);
            }
        }

        return value.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.SerializeToElement((string?)null)
            : value.Clone();
    }

    private static NodeExecutionResult Continue(IReadOnlyDictionary<string, JsonElement>? patch = null)
    {
        return new NodeExecutionResult.Continue("default", patch ?? new Dictionary<string, JsonElement>());
    }
}
