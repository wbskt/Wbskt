using System.Text.Json;
using Webskt.Workflow.Abstraction.Enums;
using Webskt.Workflow.Abstraction.Models.Expressions;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models.TriggerContexts;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services;

internal sealed class WorkflowExpressionEvaluator : IWorkflowExpressionEvaluator
{
    private readonly ILogger<WorkflowExpressionEvaluator> _logger;

    public WorkflowExpressionEvaluator(ILogger<WorkflowExpressionEvaluator> logger)
    {
        _logger = logger;
    }

    public object? Evaluate(WorkflowExpression? expression, ExecutionContext context)
    {
        try
        {
            return expression switch
            {
                LiteralExpression lit => lit.Value,

                MemberAccessExpression acc => ResolvePath(acc.Path, context),

                UnaryExpression un => EvaluateUnary(un.Operator, Evaluate(un.Operand, context)),

                BinaryExpression bin => EvaluateBinary(bin.Operator, Evaluate(bin.Left, context), Evaluate(bin.Right, context)),

                FunctionExpression fn => ExecuteFunction(fn.Function, fn.Arguments, context),

                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error evaluating expression of type {ExprType}", expression?.GetType().Name);
            return null;
        }
    }

    private static object? ResolvePath(string path, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var parts = path.Split('.');
        var root = parts[0].ToLowerInvariant();

        var current = root switch
        {
            "$trigger" => GetTriggerData(context.TriggerContext),
            "$state" => null, // Root $state handled below
            "$output" => context.LastNodeOutput,
            _ => null
        };

        // Special handling for $state as it's a dictionary lookup
        if (root == "$state" && parts.Length > 1)
        {
            current = context.GetState(parts[1]);
            if (parts.Length == 2)
            {
                return current;
            }

            return ResolveValuePath(current, parts.Skip(2));
        }

        if (parts.Length == 1)
        {
            return current;
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

            if (current is JsonElement { ValueKind: JsonValueKind.Object } element)
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
            // Add reflection-based property access here if needed for POCOs
            else
            {
                return null;
            }
        }

        return NormalizeJsonValue(current);
    }

    private static object? NormalizeJsonValue(object? value)
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

    private static object? EvaluateUnary(UnaryOperator op, object? operand)
    {
        return op switch
        {
            UnaryOperator.Not => !AsBool(operand),
            UnaryOperator.Negate => -AsDouble(operand),
            UnaryOperator.IsNull => operand == null,
            UnaryOperator.IsEmpty => string.IsNullOrEmpty(operand?.ToString()),
            _ => null
        };
    }

    private static object? EvaluateBinary(BinaryOperator op, object? left, object? right)
    {
        return op switch
        {
            // Math
            BinaryOperator.Add => AsDouble(left) + AsDouble(right),
            BinaryOperator.Subtract => AsDouble(left) - AsDouble(right),
            BinaryOperator.Multiply => AsDouble(left) * AsDouble(right),
            BinaryOperator.Divide => AsDouble(left) / AsDouble(right),

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

            _ => null
        };
    }

    private object? ExecuteFunction(FunctionName fn, List<WorkflowExpression> args, ExecutionContext context)
    {
        // Simple implementations for MVP
        return fn switch
        {
            FunctionName.Now => DateTime.Now,
            FunctionName.UtcNow => DateTime.UtcNow,
            FunctionName.Round => Math.Round(AsDouble(Evaluate(args.FirstOrDefault(), context))),
            _ => null
        };
    }

    private static bool AsBool(object? value) => value is true;
    private static double AsDouble(object? value) => Convert.ToDouble(value ?? 0);
}
