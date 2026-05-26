using FluentAssertions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class ScheduledFireProviderTests
{
    [Fact]
    public void Map_reads_all_columns_from_reader()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 42,
                ["TriggerNodeId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WorkflowDefinitionId"] = 10,
                ["WorkflowRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["CronOrInterval"] = "0 */5 * * * *",
                ["NextFireAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                ["LeasedUntil"] = new DateTime(2026, 5, 26, 12, 1, 0, DateTimeKind.Utc),
                ["CreatedAt"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = ScheduledFireProvider.Map(reader);

        row.Id.Should().Be(42);
        row.TriggerNodeId.Should().Be(Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.WorkflowDefinitionId.Should().Be(10);
        row.WorkflowRefId.Should().Be(Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"));
        row.CronOrInterval.Should().Be("0 */5 * * * *");
        row.NextFireAt.Should().Be(new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));
        row.LeasedUntil.Should().Be(new DateTime(2026, 5, 26, 12, 1, 0, DateTimeKind.Utc));
        row.CreatedAt.Should().Be(new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Map_handles_nullable_columns()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 42,
                ["TriggerNodeId"] = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["WorkflowDefinitionId"] = 10,
                ["WorkflowRefId"] = Guid.Parse("a47ac10b-58cc-4372-a567-0e02b2c3d479"),
                ["CronOrInterval"] = "0 */5 * * * *",
                ["NextFireAt"] = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
                ["LeasedUntil"] = null,
                ["CreatedAt"] = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc)
            }
        });

        reader.Read();
        var row = ScheduledFireProvider.Map(reader);

        row.LeasedUntil.Should().BeNull();
    }
}
