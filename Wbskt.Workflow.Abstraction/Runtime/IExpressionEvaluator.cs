using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IExpressionEvaluator
{
    Task<JsonElement> EvaluateAsync(WorkflowExpression expr, BranchContext context, CancellationToken ct);
}
