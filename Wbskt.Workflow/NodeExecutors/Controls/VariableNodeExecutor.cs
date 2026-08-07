using System.Globalization;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

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
    private const string CounterVarType = "Counter";
    private const string JsonVarType = "Json";

    /// <summary>Branch-state key carrying whether the last CompareAndSet on this branch won its race.</summary>
    internal const string CompareAndSetResultKey = "casSucceeded";

    private readonly ISharedVariableProvider _sharedVariableProvider;
    private readonly IExpressionEvaluator _expressionEvaluator;

    public VariableNodeExecutor(ISharedVariableProvider sharedVariableProvider, IExpressionEvaluator expressionEvaluator)
    {
        _sharedVariableProvider = sharedVariableProvider;
        _expressionEvaluator = expressionEvaluator;
    }

    public string Kind => NodeKind.ControlVariable;

    public bool IsSideEffectFree => true;

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

        string valueJson = JsonSerializer.Serialize(value);
        try
        {
            await _sharedVariableProvider.SetAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, valueJson, ct);
        }
        catch (KeyNotFoundException)
        {
            // SharedVariable_Set only UPDATEs, so a variable that was never written needs creating.
            await _sharedVariableProvider.InitializeAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, JsonVarType, valueJson, ct);
        }

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

        JsonElement newValue = await ResolveValueAsync(ctx, config, config.Value, ct);
        JsonElement expected = await ResolveValueAsync(ctx, config, config.Expected, ct);

        // Comparison is on serialized JSON text, because that is exactly what SharedVariable_CompareAndSet
        // does in SQL (ValueJson = @Expected). Matching it keeps the two scopes honest about each other
        // rather than having local quietly accept a structural match the shared path would reject.
        string expectedJson = JsonSerializer.Serialize(expected);
        string newValueJson = JsonSerializer.Serialize(newValue);

        bool succeeded;
        if (config.Scope == VariableScope.Local)
        {
            string currentJson = ctx.Branch.LocalState.TryGetValue(config.Var, out JsonElement current)
                ? JsonSerializer.Serialize(current)
                : JsonSerializer.Serialize(JsonSerializer.SerializeToElement((string?)null));

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

        if (config.Scope == VariableScope.Local)
        {
            double current = ctx.Branch.LocalState.TryGetValue(config.Var, out JsonElement existing) && existing.ValueKind == JsonValueKind.Number
                ? existing.GetDouble()
                : 0;

            return Continue(new Dictionary<string, JsonElement>
            {
                [config.Var] = JsonSerializer.SerializeToElement(current + delta)
            });
        }

        try
        {
            // Atomic in the UPDATE - no read-modify-write race, so no retry loop.
            if (delta >= 0)
            {
                await _sharedVariableProvider.IncrementAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, delta, ct);
            }
            else
            {
                await _sharedVariableProvider.DecrementAsync(ctx.Branch.WorkflowDefinitionRefId, config.Var, -delta, ct);
            }
        }
        catch (KeyNotFoundException)
        {
            // No counter yet (or it exists with a non-Counter type, which the procedures also skip).
            // Seed it at the delta, matching "started from zero then applied this step".
            await _sharedVariableProvider.InitializeAsync(
                ctx.Branch.WorkflowDefinitionRefId,
                config.Var,
                CounterVarType,
                delta.ToString(CultureInfo.InvariantCulture),
                ct);
        }

        return Continue();
    }

    private Task<JsonElement> ResolveValueAsync(NodeContext ctx, VariableConfig config, CancellationToken ct)
    {
        return ResolveValueAsync(ctx, config, config.Value, ct);
    }

    /// <summary>
    /// Resolves a configured value, evaluating it when it is an expression tree (detected by the
    /// polymorphic "kind" discriminator) rather than a plain JSON literal.
    /// </summary>
    private async Task<JsonElement> ResolveValueAsync(NodeContext ctx, VariableConfig config, JsonElement? configured, CancellationToken ct)
    {
        _ = config;
        JsonElement value = configured?.Clone() ?? JsonSerializer.SerializeToElement((string?)null);

        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("kind", out JsonElement kindProperty)
            && kindProperty.ValueKind == JsonValueKind.String)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var expression = JsonSerializer.Deserialize<WorkflowExpression>(value.GetRawText(), options);
            if (expression is not null)
            {
                value = await _expressionEvaluator.EvaluateAsync(expression, ctx.Branch, ct);
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
