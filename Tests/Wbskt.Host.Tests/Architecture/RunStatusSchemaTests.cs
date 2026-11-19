using System.Reflection;
using System.Text.RegularExpressions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Host.Tests.Architecture;

/// <summary>
/// The run statuses are written twice: as <see cref="RunStatus"/>'s constants and as dbo.Runs'
/// CK_Runs_Status. A status added to one and not the other is refused by the table or never written,
/// so the two must list the same words. The in-flight list every index filter and sweep repeats must
/// be exactly the statuses that are not terminal.
/// </summary>
public sealed class RunStatusSchemaTests
{
    private static readonly string[] InFlight = [RunStatus.Running, RunStatus.Failing, RunStatus.Cancelling];

    [Fact]
    public void The_runs_table_allows_exactly_the_statuses_RunStatus_defines()
    {
        string table = File.ReadAllText(Path.Combine(RepoPaths.Root(), "Databases", "Wbskt.Database", "Tables", "Runs.sql"));
        Match check = Regex.Match(table, @"CONSTRAINT CK_Runs_Status CHECK \((?<body>[^;]*?)\),\s*\n", RegexOptions.Singleline);
        Assert.True(check.Success, "CK_Runs_Status was not found in Runs.sql.");

        IEnumerable<string> allowed = Regex.Matches(check.Groups["body"].Value, @"N'(\w+)'").Select(m => m.Groups[1].Value);

        Assert.Equal(Defined().Order(StringComparer.Ordinal), allowed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_in_flight_list_the_procedures_repeat_is_every_status_that_is_not_terminal()
    {
        string orphans = File.ReadAllText(Path.Combine(RepoPaths.Root(), "Databases", "Wbskt.Database", "StoredProcedures", "Bookmark_DeleteOrphans.sql"));

        Assert.Contains("NOT IN (N'Running', N'Failing', N'Cancelling')", orphans);
        Assert.All(InFlight, status => Assert.Contains(status, Defined()));
        Assert.All(Defined().Except(InFlight), status => Assert.True(RunStatus.IsTerminal(status), status));
        Assert.All(InFlight, status => Assert.False(RunStatus.IsTerminal(status), status));
    }

    private static IEnumerable<string> Defined() =>
        typeof(RunStatus).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}
