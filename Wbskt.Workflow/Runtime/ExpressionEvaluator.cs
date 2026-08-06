using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Path;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models.Expressions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Evaluates a workflow expression tree against a branch's state.
///
/// <para><b>Type rules (deliberately strict, no implicit coercion).</b> A value is compared only
/// against a value of the same JSON kind:</para>
/// <list type="bullet">
/// <item>numbers compare numerically; strings compare ordinally; booleans and null support only
/// equality.</item>
/// <item>objects and arrays support only equality, compared structurally (property order is
/// irrelevant).</item>
/// <item><c>Equal</c>/<c>NotEqual</c> across different kinds is simply <i>not equal</i> - it is not an
/// error. Ordering (<c>&lt;</c>, <c>&gt;</c>, ...) across different kinds <i>is</i> an error, because
/// there is no defensible answer.</item>
/// </list>
/// <para>Notably the string <c>"5"</c> does not equal the number <c>5</c>. Payloads that carry numbers
/// as strings must be converted explicitly. Silent coercion was rejected: it turns a visible type
/// mismatch into a workflow that quietly takes the wrong branch.</para>
///
/// <para>A malformed expression raises <see cref="ExpressionEvaluationException"/>, which the retry
/// executor turns into a non-retryable <c>Fail("EXPRESSION_EVALUATION_ERROR")</c> - never an
/// unhandled crash.</para>
/// </summary>
internal sealed class ExpressionEvaluator : IExpressionEvaluator
{
    private static readonly JsonElement JsonNull = JsonSerializer.SerializeToElement((string?)null);

    private readonly IClock _clock;
    private readonly ISharedVariableProvider? _sharedVariableProvider;

    /// <param name="sharedVariableProvider">
    /// Optional so the many executor unit tests that build an evaluator by hand need not stub it.
    /// The engine always supplies one; without it, a <c>$shared</c> reference fails with a clear
    /// message rather than silently reading as null.
    /// </param>
    public ExpressionEvaluator(IClock clock, ISharedVariableProvider? sharedVariableProvider = null)
    {
        _clock = clock;
        _sharedVariableProvider = sharedVariableProvider;
    }

    public async Task<JsonElement> EvaluateAsync(WorkflowExpression expr, BranchContext context, CancellationToken ct)
    {
        return expr switch
        {
            LiteralExpression literal => ToJsonElement(literal.Value),
            BranchStateRefExpression branchStateRef => ResolveBranchState(branchStateRef.Path, context),
            MemberAccessExpression member => ResolveBranchState($"{member.Object}.{member.Member}", context),
            JsonPathExpression jsonPathExpr => await EvaluateJsonPathAsync(jsonPathExpr, context, ct),
            UnaryExpression unary => await EvaluateUnaryAsync(unary, context, ct),
            BinaryExpression binary => await EvaluateBinaryAsync(binary, context, ct),
            FunctionExpression function => await EvaluateFunctionAsync(function, context, ct),
            SharedVariableRefExpression sharedRef => await ResolveSharedVariableAsync(sharedRef.Name, context, ct),
            TemplateExpression template => await EvaluateTemplateAsync(template, context, ct),
            _ => throw NotImplemented(expr)
        };
    }

    // ---------------------------------------------------------------- unary

    private async Task<JsonElement> EvaluateUnaryAsync(UnaryExpression unary, BranchContext context, CancellationToken ct)
    {
        JsonElement operand = await EvaluateAsync(unary.Operand, context, ct);

        return unary.Operator switch
        {
            UnaryOperator.Not => ToJsonElement(!RequireBoolean(operand, "Not")),
            UnaryOperator.Negate => ToJsonElement(-RequireNumber(operand, "Negate")),
            _ => throw new ExpressionEvaluationException($"Unary operator '{unary.Operator}' is not supported.")
        };
    }

    // --------------------------------------------------------------- binary

    private async Task<JsonElement> EvaluateBinaryAsync(BinaryExpression binary, BranchContext context, CancellationToken ct)
    {
        // And/Or short-circuit: the right operand is not evaluated when the left already decides the
        // result, so a guard like `isPresent && value > 10` cannot fail on the missing value.
        if (binary.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            bool left = RequireBoolean(await EvaluateAsync(binary.Left, context, ct), binary.Operator.ToString());

            if (binary.Operator == BinaryOperator.And && !left)
            {
                return ToJsonElement(false);
            }

            if (binary.Operator == BinaryOperator.Or && left)
            {
                return ToJsonElement(true);
            }

            return ToJsonElement(RequireBoolean(await EvaluateAsync(binary.Right, context, ct), binary.Operator.ToString()));
        }

        JsonElement leftValue = await EvaluateAsync(binary.Left, context, ct);
        JsonElement rightValue = await EvaluateAsync(binary.Right, context, ct);

        if (binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual)
        {
            bool equal = JsonEquals(leftValue, rightValue);
            return ToJsonElement(binary.Operator == BinaryOperator.Equal ? equal : !equal);
        }

        int comparison = CompareOrdered(leftValue, rightValue, binary.Operator);

        return ToJsonElement(binary.Operator switch
        {
            BinaryOperator.GreaterThan => comparison > 0,
            BinaryOperator.GreaterThanOrEqual => comparison >= 0,
            BinaryOperator.LessThan => comparison < 0,
            BinaryOperator.LessThanOrEqual => comparison <= 0,
            _ => throw new ExpressionEvaluationException($"Binary operator '{binary.Operator}' is not supported.")
        });
    }

    /// <summary>Ordering comparison. Only like-for-like kinds are orderable.</summary>
    private static int CompareOrdered(JsonElement left, JsonElement right, BinaryOperator op)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
        {
            return left.GetDouble().CompareTo(right.GetDouble());
        }

        if (left.ValueKind == JsonValueKind.String && right.ValueKind == JsonValueKind.String)
        {
            return string.CompareOrdinal(left.GetString(), right.GetString());
        }

        throw new ExpressionEvaluationException(
            $"Operator '{op}' needs two numbers or two strings, but got {Describe(left)} and {Describe(right)}. " +
            "Values are never coerced - convert explicitly (e.g. with a ToString/parse step) if the comparison is intended.");
    }

    /// <summary>Structural equality. Property order is irrelevant; different kinds are never equal.</summary>
    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (IsNullish(left) && IsNullish(right))
        {
            return true;
        }

        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Number:
                return left.GetDouble().Equals(right.GetDouble());

            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;

            case JsonValueKind.Array:
            {
                if (left.GetArrayLength() != right.GetArrayLength())
                {
                    return false;
                }

                JsonElement.ArrayEnumerator leftItems = left.EnumerateArray();
                JsonElement.ArrayEnumerator rightItems = right.EnumerateArray();
                while (leftItems.MoveNext() && rightItems.MoveNext())
                {
                    if (!JsonEquals(leftItems.Current, rightItems.Current))
                    {
                        return false;
                    }
                }

                return true;
            }

            case JsonValueKind.Object:
            {
                int leftCount = 0;
                foreach (JsonProperty property in left.EnumerateObject())
                {
                    leftCount++;
                    if (!right.TryGetProperty(property.Name, out JsonElement rightProperty) || !JsonEquals(property.Value, rightProperty))
                    {
                        return false;
                    }
                }

                int rightCount = right.EnumerateObject().Count();
                return leftCount == rightCount;
            }

            default:
                return false;
        }
    }

    // ------------------------------------------------------------- functions

    private async Task<JsonElement> EvaluateFunctionAsync(FunctionExpression function, BranchContext context, CancellationToken ct)
    {
        var arguments = new List<JsonElement>(function.Arguments.Count);
        foreach (WorkflowExpression argument in function.Arguments)
        {
            arguments.Add(await EvaluateAsync(argument, context, ct));
        }

        switch (function.Function)
        {
            // The engine has no per-workspace timezone, so Now is UTC - same value as UtcNow. Both are
            // kept so a definition reads naturally; the round-trippable "O" format parses back cleanly.
            case FunctionName.Now:
            case FunctionName.UtcNow:
                RequireArgumentCount(function, arguments, 0);
                return ToJsonElement(_clock.UtcNow.ToString("O", CultureInfo.InvariantCulture));

            case FunctionName.ToString:
                RequireArgumentCount(function, arguments, 1);
                return ToJsonElement(ToStringValue(arguments[0]));

            case FunctionName.ToLower:
                RequireArgumentCount(function, arguments, 1);
                return ToJsonElement(ToStringValue(arguments[0]).ToLowerInvariant());

            case FunctionName.ToUpper:
                RequireArgumentCount(function, arguments, 1);
                return ToJsonElement(ToStringValue(arguments[0]).ToUpperInvariant());

            case FunctionName.Contains:
                RequireArgumentCount(function, arguments, 2);
                return ToJsonElement(EvaluateContains(arguments[0], arguments[1]));

            case FunctionName.StartsWith:
                RequireArgumentCount(function, arguments, 2);
                return ToJsonElement(ToStringValue(arguments[0]).StartsWith(ToStringValue(arguments[1]), StringComparison.Ordinal));

            case FunctionName.EndsWith:
                RequireArgumentCount(function, arguments, 2);
                return ToJsonElement(ToStringValue(arguments[0]).EndsWith(ToStringValue(arguments[1]), StringComparison.Ordinal));

            case FunctionName.IsNull:
                RequireArgumentCount(function, arguments, 1);
                return ToJsonElement(IsNullish(arguments[0]));

            case FunctionName.IsEmpty:
                RequireArgumentCount(function, arguments, 1);
                return ToJsonElement(IsEmpty(arguments[0]));

            default:
                throw new ExpressionEvaluationException($"Function '{function.Function}' is not supported.");
        }
    }

    /// <summary>Substring test for strings; membership test for arrays.</summary>
    private static bool EvaluateContains(JsonElement haystack, JsonElement needle)
    {
        if (haystack.ValueKind == JsonValueKind.Array)
        {
            return haystack.EnumerateArray().Any(item => JsonEquals(item, needle));
        }

        if (haystack.ValueKind == JsonValueKind.String)
        {
            return haystack.GetString()?.Contains(ToStringValue(needle), StringComparison.Ordinal) == true;
        }

        throw new ExpressionEvaluationException(
            $"Contains needs a string or an array as its first argument, but got {Describe(haystack)}.");
    }

    private static bool IsEmpty(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            JsonValueKind.String => string.IsNullOrEmpty(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            JsonValueKind.Object => !value.EnumerateObject().Any(),
            _ => false
        };
    }

    private static string ToStringValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.Number => value.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText()
        };
    }

    // --------------------------------------------------------------- guards

    private static bool RequireBoolean(JsonElement value, string operation)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ExpressionEvaluationException(
                $"Operator '{operation}' needs a boolean operand, but got {Describe(value)}. " +
                "Non-boolean values are never treated as truthy - compare explicitly instead.")
        };
    }

    private static double RequireNumber(JsonElement value, string operation)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        throw new ExpressionEvaluationException($"Operator '{operation}' needs a numeric operand, but got {Describe(value)}.");
    }

    private static void RequireArgumentCount(FunctionExpression function, List<JsonElement> arguments, int expected)
    {
        if (arguments.Count != expected)
        {
            throw new ExpressionEvaluationException(
                $"Function '{function.Function}' takes {expected} argument(s), but {arguments.Count} were supplied.");
        }
    }

    private static bool IsNullish(JsonElement value)
    {
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    }

    private static string Describe(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "null",
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            JsonValueKind.Number => "a number",
            JsonValueKind.String => "a string",
            JsonValueKind.Array => "an array",
            JsonValueKind.Object => "an object",
            _ => "an unknown value"
        };
    }

    private static ExpressionEvaluationException NotImplemented(WorkflowExpression expr)
    {
        return new ExpressionEvaluationException($"Expression type {expr.GetType().Name} not yet implemented");
    }

    // ------------------------------------------------- shared vars, templates

    /// <summary>
    /// Reads a workflow-scoped shared variable. A variable that has never been written resolves to
    /// JSON null rather than failing - the same way a missing branch-state path behaves - so a
    /// workflow can test <c>isNull($shared.counter)</c> before initialising it.
    /// </summary>
    private async Task<JsonElement> ResolveSharedVariableAsync(string name, BranchContext context, CancellationToken ct)
    {
        if (_sharedVariableProvider is null)
        {
            throw new ExpressionEvaluationException(
                $"Shared variable '{name}' cannot be read: this evaluator was built without a shared-variable provider.");
        }

        SharedVariableRow row;
        try
        {
            row = await _sharedVariableProvider.GetByWorkflowRefIdNameAsync(context.WorkflowDefinitionRefId, name, ct);
        }
        catch (KeyNotFoundException)
        {
            return JsonNull;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(row.ValueJson);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ExpressionEvaluationException(
                $"Shared variable '{name}' does not hold valid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Interpolates <c>{{ ... }}</c> placeholders into a string. A placeholder holds either
    /// <c>$shared.NAME</c> or a branch-state path (the same resolution as a branchStateRef, so it
    /// falls back to the trigger payload). Missing values render as an empty string; the result is
    /// always a JSON string.
    /// </summary>
    private async Task<JsonElement> EvaluateTemplateAsync(TemplateExpression template, BranchContext context, CancellationToken ct)
    {
        string source = template.Template ?? string.Empty;
        var builder = new System.Text.StringBuilder(source.Length);

        int index = 0;
        while (index < source.Length)
        {
            int open = source.IndexOf("{{", index, StringComparison.Ordinal);
            if (open < 0)
            {
                builder.Append(source, index, source.Length - index);
                break;
            }

            builder.Append(source, index, open - index);

            int close = source.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                throw new ExpressionEvaluationException(
                    $"Template has an unclosed '{{{{' placeholder: \"{source}\".");
            }

            string reference = source[(open + 2)..close].Trim();
            if (reference.Length == 0)
            {
                throw new ExpressionEvaluationException($"Template contains an empty placeholder: \"{source}\".");
            }

            builder.Append(ToStringValue(await ResolveTemplateReferenceAsync(reference, context, ct)));
            index = close + 2;
        }

        return ToJsonElement(builder.ToString());
    }

    private async Task<JsonElement> ResolveTemplateReferenceAsync(string reference, BranchContext context, CancellationToken ct)
    {
        const string sharedPrefix = "$shared.";
        if (reference.StartsWith(sharedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveSharedVariableAsync(reference[sharedPrefix.Length..], context, ct);
        }

        return ResolveBranchState(reference, context);
    }

    // ------------------------------------------------------------ resolution

    private static JsonElement ResolveBranchState(string path, BranchContext context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return JsonNull;
        }

        string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return JsonNull;
        }

        if (!context.LocalState.TryGetValue(segments[0], out JsonElement current) &&
            !context.TriggerPayload.TryGetValue(segments[0], out current))
        {
            return JsonNull;
        }

        for (int index = 1; index < segments.Length; index++)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segments[index], out JsonElement next))
            {
                return JsonNull;
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
            return JsonNull;
        }

        JsonNode? baseNode;
        try
        {
            // Note: Since baseElement is an in-memory DOM, we can parse its raw text.
            baseNode = JsonNode.Parse(baseElement.GetRawText());
        }
        catch
        {
            return JsonNull;
        }

        if (baseNode == null)
        {
            return JsonNull;
        }

        if (!JsonPath.TryParse(jsonPathExpr.Path, out JsonPath? path))
        {
            throw new ExpressionEvaluationException($"Invalid JSONPath expression: {jsonPathExpr.Path}");
        }

        PathResult result = path.Evaluate(baseNode);
        if (result.Matches == null || result.Matches.Count == 0)
        {
            return JsonNull;
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
}
