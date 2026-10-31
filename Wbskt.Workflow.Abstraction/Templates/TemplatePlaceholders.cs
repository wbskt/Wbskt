using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Templates;

/// <summary>
/// The <c>{{ ... }}</c> placeholder syntax, shared by <see cref="TemplateExpression"/>, the action steps'
/// <see cref="TemplatedAttribute">templated fields</see> and the validator, so the three cannot drift
/// apart. A placeholder holds <c>$shared.NAME</c> or a branch-state path.
/// </summary>
public static class TemplatePlaceholders
{
    private const string Open = "{{";
    private const string Close = "}}";
    private const string SharedPrefix = "$shared.";

    public readonly record struct Part(string Text, bool IsPlaceholder);

    public static bool Contains(string? source) => source?.Contains(Open, StringComparison.Ordinal) == true;

    /// <summary>The text split into literal runs and placeholder references (trimmed).</summary>
    /// <exception cref="ExpressionEvaluationException">An unclosed or empty placeholder.</exception>
    public static IReadOnlyList<Part> Parse(string source)
    {
        return TryParse(source, out IReadOnlyList<Part> parts, out string? error)
            ? parts
            : throw new ExpressionEvaluationException(error);
    }

    public static bool TryParse(string source, out IReadOnlyList<Part> parts, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
    {
        var found = new List<Part>();
        parts = found;
        error = null;

        int index = 0;
        while (index < source.Length)
        {
            int open = source.IndexOf(Open, index, StringComparison.Ordinal);
            if (open < 0)
            {
                found.Add(new Part(source[index..], false));
                break;
            }

            if (open > index)
            {
                found.Add(new Part(source[index..open], false));
            }

            int close = source.IndexOf(Close, open + Open.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                error = $"Template has an unclosed '{{{{' placeholder: \"{source}\".";
                return false;
            }

            string reference = source[(open + Open.Length)..close].Trim();
            if (reference.Length == 0)
            {
                error = $"Template contains an empty placeholder: \"{source}\".";
                return false;
            }

            found.Add(new Part(reference, true));
            index = close + Close.Length;
        }

        return true;
    }

    /// <summary>The reference when the whole text is one placeholder and nothing else.</summary>
    public static string? Sole(string source)
    {
        if (!source.StartsWith(Open, StringComparison.Ordinal) || !source.EndsWith(Close, StringComparison.Ordinal))
        {
            return null;
        }

        return Parse(source) is [{ IsPlaceholder: true } only] ? only.Text : null;
    }

    /// <summary>
    /// True when the reference has a form that can find a value: <c>$shared.NAME</c>, or a dotted path
    /// of names such as <c>trigger.payload.room</c>. Anything else (an operator, a space, another
    /// <c>$</c> root) is looked up as one key that no branch has, and fills in blank every time.
    /// </summary>
    public static bool IsReference(string reference)
    {
        string path = reference.StartsWith(SharedPrefix, StringComparison.OrdinalIgnoreCase)
            ? reference[SharedPrefix.Length..]
            : reference;
        return path.Length > 0 && path.Split('.').All(IsName);
    }

    private static bool IsName(string segment) =>
        segment.Length > 0 && segment.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    public static WorkflowExpression ToExpression(string reference)
    {
        return reference.StartsWith(SharedPrefix, StringComparison.OrdinalIgnoreCase)
            ? new SharedVariableRefExpression(reference[SharedPrefix.Length..])
            : new BranchStateRefExpression(reference);
    }
}
