using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Evaluates an expression from a step's config. A branch-state path it reads that is not there still
/// reads as null, as it always has, but the step also gets a
/// <see cref="HistoryEventKind.BranchStatePathMissing"/> warning naming the config field and the paths,
/// so a misspelled <c>trigger.tmep</c> shows in history instead of quietly steering the run.
/// </summary>
internal static class StepExpressions
{
    public static async Task<JsonElement> EvaluateForStepAsync(
        this IExpressionEvaluator evaluator, WorkflowExpression expression, NodeContext ctx, string field, CancellationToken ct)
    {
        var missing = new List<string>();
        JsonElement value = await evaluator.EvaluateAsync(expression, ctx.Branch, missing, ct);
        if (missing.Count > 0)
        {
            ctx.Warnings.Add(new NodeWarning(HistoryEventKind.BranchStatePathMissing, new { field, paths = missing.Distinct().ToArray() }));
        }

        return value;
    }
}
