using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The <c>{{ ... }}</c> placeholder syntax, shared by <see cref="TemplateExpression"/> and the action
/// steps' text fields so the two cannot drift apart. A placeholder holds <c>$shared.NAME</c> or a
/// branch-state path.
/// </summary>
internal static class TemplatePlaceholders
{
    private const string Open = "{{";
    private const string Close = "}}";
    private const string SharedPrefix = "$shared.";

    public readonly record struct Part(string Text, bool IsPlaceholder);

    public static bool Contains(string? source) => source?.Contains(Open, StringComparison.Ordinal) == true;

    /// <summary>The text split into literal runs and placeholder references (trimmed).</summary>
    public static IReadOnlyList<Part> Parse(string source)
    {
        var parts = new List<Part>();
        int index = 0;
        while (index < source.Length)
        {
            int open = source.IndexOf(Open, index, StringComparison.Ordinal);
            if (open < 0)
            {
                parts.Add(new Part(source[index..], false));
                break;
            }

            if (open > index)
            {
                parts.Add(new Part(source[index..open], false));
            }

            int close = source.IndexOf(Close, open + Open.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                throw new ExpressionEvaluationException($"Template has an unclosed '{{{{' placeholder: \"{source}\".");
            }

            string reference = source[(open + Open.Length)..close].Trim();
            if (reference.Length == 0)
            {
                throw new ExpressionEvaluationException($"Template contains an empty placeholder: \"{source}\".");
            }

            parts.Add(new Part(reference, true));
            index = close + Close.Length;
        }

        return parts;
    }

    /// <summary>The reference when the whole text is one placeholder and nothing else.</summary>
    public static string? Sole(string source)
    {
        if (!source.StartsWith(Open, StringComparison.Ordinal) || !source.EndsWith(Close, StringComparison.Ordinal))
        {
            return null;
        }

        IReadOnlyList<Part> parts = Parse(source);
        return parts is [{ IsPlaceholder: true } only] ? only.Text : null;
    }

    public static WorkflowExpression ToExpression(string reference)
    {
        return reference.StartsWith(SharedPrefix, StringComparison.OrdinalIgnoreCase)
            ? new SharedVariableRefExpression(reference[SharedPrefix.Length..])
            : new BranchStateRefExpression(reference);
    }
}
