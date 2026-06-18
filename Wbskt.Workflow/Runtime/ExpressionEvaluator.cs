using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Path;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class ExpressionEvaluator : IExpressionEvaluator
{
    public async Task<JsonElement> EvaluateAsync(WorkflowExpression expr, BranchContext context, CancellationToken ct)
    {
        JsonElement result = expr switch
        {
            LiteralExpression literal => ToJsonElement(literal.Value),
            BranchStateRefExpression branchStateRef => ResolveBranchState(branchStateRef.Path, context),
            JsonPathExpression jsonPathExpr => await EvaluateJsonPathAsync(jsonPathExpr, context, ct),
            SharedVariableRefExpression or TemplateExpression => throw CreateNotImplemented(expr),
            _ => throw CreateNotImplemented(expr)
        };

        return result;
    }

    private static JsonElement ResolveBranchState(string path, BranchContext context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return JsonSerializer.SerializeToElement((string?)null);
        }

        string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return JsonSerializer.SerializeToElement((string?)null);
        }

        if (!context.LocalState.TryGetValue(segments[0], out JsonElement current) &&
            !context.TriggerPayload.TryGetValue(segments[0], out current))
        {
            return JsonSerializer.SerializeToElement((string?)null);
        }

        for (int index = 1; index < segments.Length; index++)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segments[index], out JsonElement next))
            {
                return JsonSerializer.SerializeToElement((string?)null);
            }

            current = next;
        }

        return current.Clone();
    }

    private async Task<JsonElement> EvaluateJsonPathAsync(JsonPathExpression jsonPathExpr, BranchContext context, CancellationToken ct)
    {
        JsonElement baseElement = await EvaluateAsync(jsonPathExpr.BaseExpression, context, ct);
        if (baseElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return JsonSerializer.SerializeToElement((string?)null);
        }

        JsonNode? baseNode;
        try
        {
            // Note: Since baseElement is an in-memory DOM, we can parse its raw text.
            baseNode = JsonNode.Parse(baseElement.GetRawText());
        }
        catch
        {
            return JsonSerializer.SerializeToElement((string?)null);
        }

        if (baseNode == null)
        {
            return JsonSerializer.SerializeToElement((string?)null);
        }

        if (!JsonPath.TryParse(jsonPathExpr.Path, out JsonPath? path))
        {
            throw new ArgumentException($"Invalid JSONPath expression: {jsonPathExpr.Path}");
        }

        PathResult result = path.Evaluate(baseNode);
        if (result.Matches == null || result.Matches.Count == 0)
        {
            return JsonSerializer.SerializeToElement((string?)null);
        }

        // Return the first match, serialized back to JsonElement
        return JsonSerializer.SerializeToElement(result.Matches[0].Value);
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
