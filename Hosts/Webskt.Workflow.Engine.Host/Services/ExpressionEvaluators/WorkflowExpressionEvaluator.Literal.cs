using Webskt.Workflow.Abstraction.Models.Expressions;

namespace Webskt.Workflow.Engine.Host.Services.ExpressionEvaluators;

public sealed partial class WorkflowExpressionEvaluator
{
    private static object? EvaluateLiteral(LiteralExpression expression)
    {
        return expression.Value;
    }
}
