using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Engine.Host.Services.ExpressionEvaluators;

public sealed partial class WorkflowExpressionEvaluator
{
    private static object? EvaluateLiteral(LiteralExpression expression)
    {
        if (expression.Value == null)
        {
            return null;
        }

        switch (expression.Value.Value.ValueKind)
        {
            case JsonValueKind.String:
            {
                return expression.Value.Value.GetString();
            }

            case JsonValueKind.Number:
            {
                // We check for double first as it is the most common JSON number format
                if (expression.Value.Value.TryGetDouble(out var d))
                {
                    return d;
                }
                return expression.Value.Value.GetRawText();
            }

            case JsonValueKind.True:
            {
                return true;
            }

            case JsonValueKind.False:
            {
                return false;
            }

            case JsonValueKind.Null:
            {
                return null;
            }

            case JsonValueKind.Object:
            case JsonValueKind.Array:
            {
                // For complex types, we usually return the JsonElement itself 
                // or a cloned version if the document might be disposed.
                return expression.Value.Value.Clone();
            }

            case JsonValueKind.Undefined:

            default:
            {
                return null;
            }
        }
    }
}
