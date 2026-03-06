using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Engine.Host.Services.ExpressionEvaluators;

public sealed partial class WorkflowExpressionEvaluator
{
    private static object? EvaluateLiteral(LiteralExpression expression)
    {
        return expression.Value;
    }
}
