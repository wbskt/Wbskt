using System.Collections;
using Webskt.Workflow.Abstraction.Enums;
using Webskt.Workflow.Abstraction.Models.Expressions;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.ExpressionEvaluators;

public sealed partial class WorkflowExpressionEvaluator
{
    private object? EvaluateBinary(BinaryExpression expression, ExecutionContext context)
    {
        var left = Evaluate(expression.Left, context);
        var right = Evaluate(expression.Right, context);

        return expression.Operator switch
        {
            // Math
            BinaryOperator.Add => AsDouble(left) + AsDouble(right),
            BinaryOperator.Subtract => AsDouble(left) - AsDouble(right),
            BinaryOperator.Multiply => AsDouble(left) * AsDouble(right),
            BinaryOperator.Divide => AsDouble(left) / AsDouble(right),
            BinaryOperator.Modulo => AsDouble(left) % AsDouble(right),

            // Comparison
            BinaryOperator.Equal => Equals(left, right),
            BinaryOperator.NotEqual => !Equals(left, right),
            BinaryOperator.GreaterThan => AsDouble(left) > AsDouble(right),
            BinaryOperator.GreaterThanOrEqual => AsDouble(left) >= AsDouble(right),
            BinaryOperator.LessThan => AsDouble(left) < AsDouble(right),
            BinaryOperator.LessThanOrEqual => AsDouble(left) <= AsDouble(right),

            // Logic
            BinaryOperator.And => AsBool(left) && AsBool(right),
            BinaryOperator.Or => AsBool(left) || AsBool(right),

            // Collection / String
            BinaryOperator.Contains => EvaluateContains(left, right),
            BinaryOperator.In => EvaluateContains(right, left),

            _ => null
        };
    }

    private static bool EvaluateContains(object? container, object? item)
    {
        if (container is string s)
        {
            return s.Contains(item?.ToString() ?? string.Empty);
        }

        if (container is IEnumerable enumerable)
        {
            foreach (var element in enumerable)
            {
                if (Equals(element, item))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
