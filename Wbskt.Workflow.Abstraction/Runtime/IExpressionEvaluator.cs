using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IExpressionEvaluator
{
    Task<JsonElement> EvaluateAsync(WorkflowExpression expr, BranchContext context, CancellationToken ct);

    /// <summary>
    /// Evaluates as above, and adds to <paramref name="missingPaths"/> each branch-state path the
    /// expression read that is not there. A path present with the value null is not missing, and nor is
    /// one read by <c>isNull</c> or <c>isEmpty</c>, which exist to test for absence. An evaluator that
    /// does not track paths reports none.
    /// </summary>
    Task<JsonElement> EvaluateAsync(WorkflowExpression expr, BranchContext context, ICollection<string>? missingPaths, CancellationToken ct) =>
        EvaluateAsync(expr, context, ct);
}
