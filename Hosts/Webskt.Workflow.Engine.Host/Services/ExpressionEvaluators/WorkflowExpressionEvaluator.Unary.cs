using Webskt.Workflow.Abstraction.Enums;
using Webskt.Workflow.Abstraction.Models.Expressions;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.ExpressionEvaluators;

public sealed partial class WorkflowExpressionEvaluator
{
    private object? EvaluateUnary(UnaryExpression expression, ExecutionContext context)
    {
        var operand = Evaluate(expression.Operand, context);

        return expression.Operator switch
        {
            UnaryOperator.Not => !AsBool(operand),
            UnaryOperator.Negate => -AsDouble(operand),
            UnaryOperator.IsNull => operand == null,
            UnaryOperator.IsEmpty => string.IsNullOrEmpty(operand?.ToString()),
            _ => null
        };
    }
}
