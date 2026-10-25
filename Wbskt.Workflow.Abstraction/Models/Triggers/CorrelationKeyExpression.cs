namespace Wbskt.Workflow.Abstraction.Models.Triggers;

/// <summary>
/// The forms a trigger's correlation key can take, read the same way by the validator and the
/// dispatcher: a path into the trigger payload, written <c>$trigger.payload.room</c> or as the
/// designer's template <c>{{trigger.payload.room}}</c>, or any other text taken as a constant.
/// </summary>
public static class CorrelationKeyExpression
{
    private const string DollarPrefix = "$trigger.";
    private const string TemplatePrefix = "trigger.";

    /// <summary>The dotted path after <c>trigger.</c>, e.g. <c>payload.room</c>, when the key is a payload path.</summary>
    public static bool TryGetTriggerPath(string? expression, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        string key = expression.Trim();
        if (key.StartsWith(DollarPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = key[DollarPrefix.Length..];
            return true;
        }

        // The whole key is one template. A template inside other text ("room-{{…}}") is not a path,
        // and the validator rejects it rather than letting it through as a constant.
        if (key.StartsWith("{{", StringComparison.Ordinal) && key.EndsWith("}}", StringComparison.Ordinal))
        {
            string inner = key[2..^2].Trim();
            if (inner.StartsWith(TemplatePrefix, StringComparison.OrdinalIgnoreCase) && !inner.Contains('{') && !inner.Contains('}'))
            {
                path = inner[TemplatePrefix.Length..];
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A key that looks like an expression but is not a payload path, e.g. <c>{{payload.room}}</c> or
    /// <c>room-{{trigger.payload.room}}</c>. Used as is it would be one constant shared by every run.
    /// </summary>
    public static bool IsUnresolvableTemplate(string? expression) =>
        !string.IsNullOrWhiteSpace(expression)
        && !TryGetTriggerPath(expression, out _)
        && (expression.Contains("{{", StringComparison.Ordinal) || expression.Contains("}}", StringComparison.Ordinal));
}
