using System.Text.RegularExpressions;

namespace Wbskt.Host.Tests.Architecture;

/// <summary>
/// Every E2E scenario is a <c>[SkippableFact]</c> or <c>[SkippableTheory]</c> whose first statement
/// decides whether it can run: <c>Skip.IfNot(...)</c>, <c>Skip.If(...)</c>, or a <c>Skip…()</c>
/// helper in the same file that itself starts with one. A scenario that does work first (registers
/// a user, opens a socket) before deciding fails when the hosts are down instead of skipping, and CI's
/// "did the suite actually run" checks can no longer tell the two apart.
/// </summary>
/// <remarks>
/// The E2E project is not referenced from here, so this reads its sources from the repository.
/// </remarks>
public sealed partial class E2ESkipRuleTests
{
    [Fact]
    public void Every_skippable_e2e_scenario_starts_by_deciding_whether_to_skip()
    {
        string project = Path.Combine(RepoPaths.Root(), "Tests", "Wbskt.E2E.FeatureTests");
        int seen = 0;
        var offending = new List<string>();
        foreach (string file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !IsBuildOutput(project, f))
                     .Order(StringComparer.Ordinal))
        {
            string source = File.ReadAllText(file);
            var helpers = SkipHelpers(source);
            foreach (Match attribute in SkippableAttribute().Matches(source))
            {
                if (IsCommentedOut(source, attribute.Index))
                {
                    continue;
                }

                seen++;
                Match declaration = MethodDeclaration().Match(source, attribute.Index + attribute.Length);
                string? first = declaration.Success ? FirstStatement(source, declaration.Index) : null;
                if (first is null || !StartsWithSkip(first, helpers))
                {
                    offending.Add($"{Path.GetRelativePath(project, file)}:{LineOf(source, attribute.Index)}");
                }
            }
        }

        // The rule reads what it finds, so pin that it found the suite.
        Assert.True(seen > 100, $"only {seen} skippable scenarios found under {project}");
        Assert.True(
            offending.Count == 0,
            "These scenarios do not start with Skip.IfNot(...) / Skip.If(...):"
            + Environment.NewLine + string.Join(Environment.NewLine, offending));
    }

    private static bool StartsWithSkip(string statement, HashSet<string> helpers)
    {
        if (statement.StartsWith("Skip.IfNot(", StringComparison.Ordinal) || statement.StartsWith("Skip.If(", StringComparison.Ordinal))
        {
            return true;
        }

        Match call = HelperCall().Match(statement);
        return call.Success && helpers.Contains(call.Groups["name"].Value);
    }

    /// <summary>The parameterless <c>Skip…()</c> methods in a file whose own first statement is a Skip.</summary>
    private static HashSet<string> SkipHelpers(string source)
    {
        var helpers = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match helper in HelperDeclaration().Matches(source))
        {
            string? first = FirstStatement(source, helper.Index);
            if (first is not null && (first.StartsWith("Skip.IfNot(", StringComparison.Ordinal) || first.StartsWith("Skip.If(", StringComparison.Ordinal)))
            {
                helpers.Add(helper.Groups["name"].Value);
            }
        }

        return helpers;
    }

    /// <summary>
    /// The source from the first statement of the method declared at <paramref name="from"/> on, or
    /// null for an expression-bodied method (which has no room for a Skip before its work).
    /// </summary>
    private static string? FirstStatement(string source, int from)
    {
        int depth = 0;
        for (int i = from; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (depth == 0 && c == '=' && i + 1 < source.Length && source[i + 1] == '>')
            {
                return null;
            }
            else if (depth == 0 && c == '{')
            {
                return LeadingTrivia().Replace(source[(i + 1)..], string.Empty);
            }
        }

        return null;
    }

    private static bool IsCommentedOut(string source, int index)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        return source[lineStart..index].Contains("//", StringComparison.Ordinal);
    }

    private static bool IsBuildOutput(string project, string file)
    {
        string relative = Path.GetRelativePath(project, file).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal);
    }

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;

    [GeneratedRegex(@"\[\s*(?:Xunit\.)?Skippable(?:Fact|Theory)\b")]
    private static partial Regex SkippableAttribute();

    [GeneratedRegex(@"^\s*(?:public|private|internal|protected)\b", RegexOptions.Multiline)]
    private static partial Regex MethodDeclaration();

    [GeneratedRegex(@"\bvoid\s+(?<name>Skip\w*)\s*\(\s*\)")]
    private static partial Regex HelperDeclaration();

    [GeneratedRegex(@"^(?<name>Skip\w*)\s*\(\s*\)\s*;")]
    private static partial Regex HelperCall();

    // Whitespace, line comments and block comments before the first statement.
    [GeneratedRegex(@"^(?:\s+|//[^\n]*|/\*.*?\*/)*", RegexOptions.Singleline)]
    private static partial Regex LeadingTrivia();
}
