using System.Text.Json;
using System.Text.RegularExpressions;
using Wbskt.Host.Tests.Architecture;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.Billing;

/// <summary>
/// The retention sweeps read a tenant's overrides in SQL by their JSON names
/// (<c>JSON_VALUE(LimitOverrides, '$.rawReadingsDays')</c>), so renaming a property of
/// <see cref="PlanLimitOverrides"/> would silently drop the override there.
/// </summary>
public sealed class PlanLimitOverridesJsonTests
{
    private static readonly Regex OverrideKey = new(@"JSON_VALUE\(\s*\w+\.LimitOverrides\s*,\s*'\$\.(\w+)'\s*\)", RegexOptions.Compiled);

    [Fact]
    public void The_retention_overrides_are_written_under_the_names_the_sweeps_read()
    {
        string json = PlanLimitOverridesJson.Write(new PlanLimitOverrides { RawReadingsDays = 1, HourlyReadingsDays = 2, RunHistoryDays = 3 })!;

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("rawReadingsDays").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("hourlyReadingsDays").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("runHistoryDays").GetInt32());
    }

    [Fact]
    public void Every_override_a_procedure_reads_is_one_the_model_writes()
    {
        string databases = Path.Combine(RepoPaths.Root(), "Databases");
        List<string> read = Directory.EnumerateFiles(databases, "*.sql", SearchOption.AllDirectories)
            .SelectMany(file => OverrideKey.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();
        HashSet<string> written = typeof(PlanLimitOverrides).GetProperties()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet();

        Assert.NotEmpty(read);
        Assert.All(read, key => Assert.Contains(key, written));
    }
}
