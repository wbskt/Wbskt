using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.ExpressionEvaluators;

public sealed partial class WorkflowExpressionEvaluator
{
    private static object? EvaluateAccess(MemberAccessExpression expression, ExecutionContext context)
    {
        var path = expression.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var parts = path.Split('.');
        var root = parts[0].ToLowerInvariant();

        object? current = root switch
        {
            "$trigger" => GetTriggerData(context.TriggerContext),
            "$state" => null, // Handled specifically for dictionary lookup
            "$output" => context.LastNodeOutput,
            _ => null
        };

        if (root == "$state" && parts.Length > 1)
        {
            current = context.GetState(parts[1]);
            if (parts.Length == 2)
            {
                return NormalizeValue(current);
            }
            return ResolveValuePath(current, parts.Skip(2));
        }

        if (parts.Length == 1)
        {
            return NormalizeValue(current);
        }

        return ResolveValuePath(current, parts.Skip(1));
    }

    private static object? GetTriggerData(BaseTriggerContext? context)
    {
        return context switch
        {
            ClientPayloadTriggerContext cpc => cpc.Data,
            ClientPropertyChangeTriggerContext dpc => dpc.NewValue,
            _ => null
        };
    }

    private static object? ResolveValuePath(object? root, IEnumerable<string> path)
    {
        var current = root;
        foreach (var part in path)
        {
            if (current == null)
            {
                return null;
            }

            if (current is JsonElement element && element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty(part, out var property))
                {
                    current = property;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                // Extend here for reflection-based POCO access if needed
                return null;
            }
        }

        return NormalizeValue(current);
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element
            };
        }
        return value;
    }
}
