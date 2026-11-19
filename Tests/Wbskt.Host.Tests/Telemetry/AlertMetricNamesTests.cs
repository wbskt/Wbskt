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
        string emitters = string.Join('\n', EmitterFiles(root).Select(File.ReadAllText));

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
