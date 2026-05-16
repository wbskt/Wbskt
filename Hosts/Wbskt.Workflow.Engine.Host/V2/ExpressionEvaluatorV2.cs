using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Engine.Host.V2;

/// <summary>
/// Evaluates <see cref="WorkflowExpression"/> trees against a V2 <see cref="BranchContext"/>.
/// <para>Supported root paths in <see cref="MemberAccessExpression"/>:</para>
/// <list type="bullet">
///   <item><c>$trigger.prop.sub</c> — navigates into the trigger payload object</item>
///   <item><c>$state.key</c>       — reads a global runtime state variable</item>
///   <item><c>$local.key</c>       — reads a branch-private variable</item>
///   <item><c>$output</c>          — the output value of the previous node</item>
/// </list>
/// </summary>
public sealed class ExpressionEvaluatorV2
{
    public object? Evaluate(WorkflowExpression expr, BranchContext ctx)
    {
        try
        {
            return expr switch
            {
                LiteralExpression lit   => EvaluateLiteral(lit),
                MemberAccessExpression acc => EvaluateAccess(acc, ctx),
                UnaryExpression un      => EvaluateUnary(un, ctx),
                BinaryExpression bin    => EvaluateBinary(bin, ctx),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    // ── Literal ───────────────────────────────────────────────────────────────

    private static object? EvaluateLiteral(LiteralExpression lit)
    {
        if (lit.Value is null) return null;
        return lit.Value.Value.ValueKind switch
        {
            JsonValueKind.String => lit.Value.Value.GetString(),
            JsonValueKind.Number => lit.Value.Value.GetDouble(),
            JsonValueKind.True   => true,
            JsonValueKind.False  => false,
            JsonValueKind.Null   => null,
            _                    => (object?)lit.Value.Value
        };
    }

    // ── Member access ─────────────────────────────────────────────────────────

    private object? EvaluateAccess(MemberAccessExpression acc, BranchContext ctx)
    {
        if (string.IsNullOrWhiteSpace(acc.Path)) return null;

        var parts = acc.Path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var root  = parts[0].ToLowerInvariant();

        return root switch
        {
            "$trigger" => parts.Length == 1
                ? ctx.TriggerPayload
                : ResolvePath(ctx.TriggerPayload, parts.Skip(1)),

            "$state" => parts.Length > 1
                ? ctx.GetGlobal(parts[1])
                : null,

            "$local" => parts.Length > 1
                ? ctx.GetLocal(parts[1])
                : null,

            "$output" => ctx.LastOutput,

            _ => null
        };
    }

    // ── Unary ─────────────────────────────────────────────────────────────────

    private object? EvaluateUnary(UnaryExpression un, BranchContext ctx)
    {
        var operand = Evaluate(un.Operand, ctx);
        return un.Operator switch
        {
            UnaryOperator.Not     => operand is not true,
            UnaryOperator.Negate  => operand is double d ? (object)-d : null,
            UnaryOperator.IsNull  => operand is null,
            UnaryOperator.IsEmpty => operand is string s ? string.IsNullOrEmpty(s) : operand is null,
            _                     => null
        };
    }

    // ── Binary ────────────────────────────────────────────────────────────────

    private object? EvaluateBinary(BinaryExpression bin, BranchContext ctx)
    {
        var left  = Evaluate(bin.Left,  ctx);
        var right = Evaluate(bin.Right, ctx);

        return bin.Operator switch
        {
            BinaryOperator.Equal              => Equals(left, right),
            BinaryOperator.NotEqual           => !Equals(left, right),
            BinaryOperator.GreaterThan        => AsDouble(left) >  AsDouble(right),
            BinaryOperator.GreaterThanOrEqual => AsDouble(left) >= AsDouble(right),
            BinaryOperator.LessThan           => AsDouble(left) <  AsDouble(right),
            BinaryOperator.LessThanOrEqual    => AsDouble(left) <= AsDouble(right),
            BinaryOperator.And                => left is true && right is true,
            BinaryOperator.Or                 => left is true || right is true,
            BinaryOperator.Add                => AsDouble(left) + AsDouble(right),
            BinaryOperator.Subtract           => AsDouble(left) - AsDouble(right),
            BinaryOperator.Multiply           => AsDouble(left) * AsDouble(right),
            BinaryOperator.Divide             => AsDouble(right) != 0
                                                    ? AsDouble(left) / AsDouble(right)
                                                    : (object?)null,
            BinaryOperator.Contains           => left?.ToString()
                                                    ?.Contains(right?.ToString() ?? "") ?? false,
            _ => null
        };
    }

    // ── Path navigation (JSON / POCO) ─────────────────────────────────────────

    private static object? ResolvePath(object? root, IEnumerable<string> parts)
    {
        var current = root;
        foreach (var part in parts)
        {
            if (current is null) return null;

            if (current is JsonElement el && el.ValueKind == JsonValueKind.Object)
            {
                if (!el.TryGetProperty(part, out var prop)) return null;
                current = prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.GetDouble(),
                    JsonValueKind.True   => true,
                    JsonValueKind.False  => false,
                    JsonValueKind.Null   => null,
                    _                    => (object?)prop
                };
            }
            else
            {
                return null; // extend here for POCO reflection if needed
            }
        }
        return current;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double AsDouble(object? v)
    {
        try   { return Convert.ToDouble(v ?? 0); }
        catch { return 0; }
    }
}

