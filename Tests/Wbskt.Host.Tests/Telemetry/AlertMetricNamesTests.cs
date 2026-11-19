using System.Text.RegularExpressions;

namespace Wbskt.Host.Tests.Telemetry;

/// <summary>
/// A rule in deploy/observability/alerts.yml that names a metric nothing emits never fires and says
/// nothing, and promtool cannot tell: renaming an instrument would silence its alert. This checks that
/// every wbskt_* name an alert reads is still written by the code or by a deploy script (#987).
/// </summary>
public sealed partial class AlertMetricNamesTests
{
    private static readonly string[] HistogramSeries = ["_bucket", "_count", "_sum"];

    [Fact]
    public void Every_metric_an_alert_reads_is_emitted()
    {
        DirectoryInfo root = RepositoryRoot();
        string alerts = File.ReadAllText(Path.Combine(root.FullName, "deploy", "observability", "alerts.yml"));
        IReadOnlyCollection<string> names = MetricNames(alerts);
        string emitters = string.Join('\n', EmitterFiles(root).Select(Emitted));

        string[] missing = names.Where(name => !IsEmitted(name, emitters)).ToArray();

        Assert.NotEmpty(names);
        Assert.True(missing.Length == 0, $"alerts.yml reads metrics nothing emits: {string.Join(", ", missing)}");
    }

    [Fact]
    public void A_name_alternation_expands_to_each_name()
    {
        IReadOnlyCollection<string> names = MetricNames("""{__name__=~"wbskt_(auth|gateway)_worker_ticks_total"} and wbskt_auth_mail_total""");

        Assert.Equal(["wbskt_auth_mail_total", "wbskt_auth_worker_ticks_total", "wbskt_gateway_worker_ticks_total"], names.Order());
    }

    private static IReadOnlyCollection<string> MetricNames(string alerts)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Alternation().Matches(alerts))
        {
            foreach (string part in match.Groups["parts"].Value.Split('|'))
            {
                names.Add($"wbskt_{part}_{match.Groups["rest"].Value}");
            }
        }

        foreach (Match match in Name().Matches(Alternation().Replace(alerts, string.Empty)))
        {
            names.Add(match.Value);
        }

        return names;
    }

    [Fact]
    public void A_name_only_in_a_comment_is_not_emitted()
    {
        string emitters = Emitted("x.cs", """"
            // Raises the "wbskt_outbox_unreadable_rows" alert when ...
            /* the "wbskt_old_block" gauge */
            char quote = '"';
            var gauge = meter.CreateObservableGauge("wbskt_outbox_unread_rows", Read);
            var path = @"C:\x\";
            var raw = """
                wbskt_raw_total "quoted"
                """;
            """") + '\n' + Emitted("x.sh", """
            # pushes wbskt_backup_last_success_seconds
            echo "wbskt_backup_age_seconds $age"
            """);

        Assert.False(IsEmitted("wbskt_outbox_unreadable_rows", emitters));
        Assert.True(IsEmitted("wbskt_outbox_unread_rows", emitters));
        Assert.False(IsEmitted("wbskt_old_block", emitters));
        Assert.True(IsEmitted("wbskt_raw_total", emitters));
        Assert.False(IsEmitted("wbskt_backup_last_success_seconds", emitters));
        Assert.True(IsEmitted("wbskt_backup_age_seconds", emitters));
    }

    // What a file can emit: the string literals of C#, and the lines of a script that are not comments,
    // so a name left in a comment after the instrument was renamed does not count.
    private static string Emitted(string path)
    {
        return Emitted(path, File.ReadAllText(path));
    }

    private static string Emitted(string path, string text)
    {
        if (path.EndsWith(".cs", StringComparison.Ordinal))
        {
            return string.Join('\n', StringLiterals(text));
        }

        return string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));
    }

    // The text of every string literal in C# source, skipping comments and char literals: regular,
    // verbatim (@"…") and raw ("""…""") strings, interpolated or not. Not a full lexer, but enough that a
    // name in a comment is not counted and a quote inside a char literal or another string kind does
    // not throw the reading out of step.
    private static IEnumerable<string> StringLiterals(string source)
    {
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int end = source.IndexOf('\n', i);
                i = end < 0 ? source.Length : end + 1;
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 2;
            }
            else if (c == '\'')
            {
                i++;
                while (i < source.Length && source[i] != '\'' && source[i] != '\n')
                {
                    i += source[i] == '\\' ? 2 : 1;
                }

                i++;
            }
            else if (c == '"')
            {
                int quotes = 0;
                while (i + quotes < source.Length && source[i + quotes] == '"')
                {
                    quotes++;
                }

                bool verbatim = i > 0 && (source[i - 1] == '@' || (i > 1 && source[i - 1] == '$' && source[i - 2] == '@'));
                if (quotes >= 3)
                {
                    string fence = new('"', quotes);
                    int start = i + quotes;
                    int end = source.IndexOf(fence, start, StringComparison.Ordinal);
                    end = end < 0 ? source.Length : end;
                    yield return source[start..end];
                    i = end + quotes;
                }
                else if (quotes == 2 && !verbatim)
                {
                    i += 2;
                }
                else
                {
                    int start = ++i;
                    while (i < source.Length)
                    {
                        if (verbatim && source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"')
                        {
                            i += 2;
                        }
                        else if (!verbatim && source[i] == '\\')
                        {
                            i += 2;
                        }
                        else if (source[i] == '"' || (!verbatim && source[i] == '\n'))
                        {
                            break;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    yield return source[start..Math.Min(i, source.Length)];
                    i++;
                }
            }
            else
            {
                i++;
            }
        }
    }

    private static bool IsEmitted(string name, string emitters)
    {
        // A histogram is created under its base name and read as its _bucket, _count and _sum series.
        string instrument = HistogramSeries.FirstOrDefault(name.EndsWith) is { } suffix ? name[..^suffix.Length] : name;
        return Regex.IsMatch(emitters, $@"\b({Regex.Escape(name)}|{Regex.Escape(instrument)})\b");
    }

    private static IEnumerable<string> EmitterFiles(DirectoryInfo root)
    {
        IEnumerable<string> code = Directory.EnumerateFiles(root.FullName, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnder(root, path, "Tests") && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        IEnumerable<string> scripts = Directory.EnumerateFiles(Path.Combine(root.FullName, "deploy", "scripts"), "*.sh");
        return code.Concat(scripts);
    }

    private static bool IsUnder(DirectoryInfo root, string path, string topLevel)
    {
        return Path.GetRelativePath(root.FullName, path).StartsWith(topLevel + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "deploy", "observability", "alerts.yml")))
            {
                return dir;
            }
        }

        throw new FileNotFoundException("deploy/observability/alerts.yml was not found above the test output.");
    }

    [GeneratedRegex(@"wbskt_\((?<parts>[a-z0-9_|]+)\)_(?<rest>[a-z0-9_]+)")]
    private static partial Regex Alternation();

    [GeneratedRegex(@"\bwbskt_[a-z0-9_]+\b")]
    private static partial Regex Name();
}
