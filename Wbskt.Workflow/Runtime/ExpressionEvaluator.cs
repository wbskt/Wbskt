using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class ExpressionEvaluator : IExpressionEvaluator
{
    public Task<JsonElement> EvaluateAsync(WorkflowExpression expr, BranchContext context, CancellationToken ct)
    {
        _ = ct;

        JsonElement result = expr switch
        {
            LiteralExpression literal => ToJsonElement(literal.Value),
            BranchStateRefExpression branchStateRef => ResolveBranchState(branchStateRef.Path, context),
            SharedVariableRefExpression or TemplateExpression or JsonPathExpression => throw CreateNotImplemented(expr),
            _ => throw CreateNotImplemented(expr)
        };

        return Task.FromResult(result);
    }

    private static JsonElement ResolveBranchState(string path, BranchContext context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return default;
        }

        string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return default;
        }

        if (!context.LocalState.TryGetValue(segments[0], out JsonElement current))
        {
            return default;
        }

        for (int index = 1; index < segments.Length; index++)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segments[index], out JsonElement next))
            {
                return default;
            }

            current = next;
        }

        return current.Clone();
    }

    private static JsonElement ToJsonElement(object? value)
    {
        if (value is JsonElement element)
        {
            return element.Clone();
        }

        return JsonSerializer.SerializeToElement(value);
    }

    private static NotImplementedException CreateNotImplemented(WorkflowExpression expr)
    {
        return new NotImplementedException($"Expression type {expr.GetType().Name} not yet implemented");
    }
}
